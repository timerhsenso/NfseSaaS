using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Contratos;

/// <summary>
/// Desfazer o último reajuste do Contrato — usado pelo botão "Desfazer" do
/// Histórico de valores (DesfazerReajusteUseCase) e pelo "Substituir o
/// reajuste anterior" do registro manual (RegistrarReajusteUseCase). As
/// regras de quando pode ficam em <see cref="RegrasDesfazerReajuste"/>.
/// Não chama SaveChanges: quem chama decide a unidade de trabalho.
/// </summary>
public static class DesfazerReajusteContrato
{
    /// <summary>Nota que conta como "usou o valor reajustado": de produção, emitida ou em emissão.</summary>
    private static readonly NfseStatus[] StatusNotaQueUsouOValor =
        { NfseStatus.Processando, NfseStatus.Autorizada, NfseStatus.Substituida };

    /// <summary>Data da primeira nota de PRODUÇÃO do Contrato desde <paramref name="desde"/>; homologação não conta.</summary>
    public static async Task<DateTimeOffset?> PrimeiraNotaProducaoDesdeAsync(
        AppDbContext db, Guid contratoId, DateTimeOffset desde, CancellationToken cancellationToken) =>
        await db.NotasFiscais.AsNoTracking()
            .Where(n => n.ContratoId == contratoId
                && n.TipoAmbiente == TipoAmbiente.Producao
                && StatusNotaQueUsouOValor.Contains(n.Status)
                && n.CreatedAt >= desde)
            .OrderBy(n => n.CreatedAt)
            .Select(n => (DateTimeOffset?)n.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>O último reajuste aplicado e, se ele não puder ser desfeito, o porquê.</summary>
    public static async Task<(MovimentacaoValorContrato? Reajuste, string? Bloqueio)> AvaliarAsync(
        AppDbContext db, Contrato contrato, IReadOnlyCollection<MovimentacaoValorContrato> eventos, CancellationToken cancellationToken)
    {
        var reajuste = RegrasDesfazerReajuste.UltimoReajusteAplicado(eventos);
        if (reajuste is null)
            return (null, null);

        var nota = await PrimeiraNotaProducaoDesdeAsync(db, contrato.Id, RegrasDesfazerReajuste.AplicadoEm(reajuste), cancellationToken);
        return (reajuste, RegrasDesfazerReajuste.MotivoBloqueio(reajuste, eventos, contrato.ValorAtual, nota));
    }

    /// <summary>
    /// Desfaz <paramref name="reajuste"/> (precisa estar rastreado pelo
    /// contexto, assim como o Contrato): valor, serviços e data do último
    /// reajuste voltam ao que eram antes dele, e o evento vira Desfeito.
    /// Lança <see cref="RegraNegocioException"/> se não puder.
    /// </summary>
    public static async Task DesfazerAsync(
        AppDbContext db,
        Contrato contrato,
        MovimentacaoValorContrato reajuste,
        string? motivo,
        Guid? usuarioId,
        CancellationToken cancellationToken)
    {
        var (motivoLimpo, erroMotivo) = RegrasDesfazerReajuste.ValidarMotivo(motivo);
        if (erroMotivo is not null)
            throw new RegraNegocioException(erroMotivo);

        var eventos = await db.MovimentacoesValorContrato.AsNoTracking()
            .Where(m => m.ContratoId == contrato.Id)
            .ToListAsync(cancellationToken);

        var (ultimo, bloqueio) = await AvaliarAsync(db, contrato, eventos, cancellationToken);
        if (ultimo is null || ultimo.Id != reajuste.Id)
            throw new RegraNegocioException(
                RegrasDesfazerReajuste.MotivoBloqueio(reajuste, eventos, contrato.ValorAtual, null)
                ?? "Só o último reajuste do contrato pode ser desfeito.");
        if (bloqueio is not null)
            throw new RegraNegocioException(bloqueio);

        var valorAnterior = reajuste.ValorAnterior
            ?? throw new RegraNegocioException("Este reajuste não tem o valor anterior registrado e não pode ser desfeito.");

        // Serviços: a composição do evento aplicado anterior (o estado de
        // antes), se ela tiver os mesmos serviços de hoje; senão (eventos
        // antigos, sem composição) desfaz a escala do reajuste nas linhas fixas.
        var anterior = eventos
            .Where(e => e.Id != reajuste.Id && e.Status == StatusMovimentacaoValor.Aplicado
                && RegrasDesfazerReajuste.AplicadoEm(e) < RegrasDesfazerReajuste.AplicadoEm(reajuste))
            .OrderByDescending(RegrasDesfazerReajuste.AplicadoEm)
            .FirstOrDefault();
        var linhas = await db.ContratoServicos.Where(cs => cs.ContratoId == contrato.Id).ToListAsync(cancellationToken);
        RestaurarLinhas(linhas, ComposicaoContrato.Desserializar(anterior?.ComposicaoJson), reajuste.ValorNovo, valorAnterior);

        contrato.ValorAtual = valorAnterior;
        contrato.DataUltimoReajuste = RegrasDesfazerReajuste.DataUltimoReajusteAoDesfazer(
            reajuste, eventos, contrato.PeriodicidadeReajusteMeses, contrato.DataInicioContrato);

        reajuste.Status = StatusMovimentacaoValor.Desfeito;
        reajuste.DesfeitoEm = DateTimeOffset.UtcNow;
        reajuste.DesfeitoPorUsuarioId = usuarioId;
        reajuste.MotivoDesfazer = motivoLimpo;
    }

    /// <summary>
    /// Volta as linhas de serviço ao estado de antes do reajuste. Com a
    /// composição anterior (mesmos serviços de hoje), copia quantidade e
    /// valor unitário. Sem ela, aplica o fator inverso nas linhas que o
    /// reajuste escalou (as fixas — ver ReajusteContrato.AplicarAsync).
    /// </summary>
    internal static void RestaurarLinhas(
        IReadOnlyCollection<ContratoServico> linhas,
        IReadOnlyList<Domain.Snapshots.ComposicaoContratoLinha> composicaoAnterior,
        decimal valorReajustado,
        decimal valorAnterior)
    {
        var mesmosServicos = composicaoAnterior.Count == linhas.Count
            && composicaoAnterior.Select(c => c.ServicoId).Distinct().Count() == composicaoAnterior.Count
            && linhas.All(l => composicaoAnterior.Any(c => c.ServicoId == l.ServicoId));

        if (mesmosServicos)
        {
            foreach (var linha in linhas)
            {
                var antes = composicaoAnterior.Single(c => c.ServicoId == linha.ServicoId);
                linha.Quantidade = antes.Quantidade;
                linha.ValorUnitario = antes.ValorUnitario;
            }
            return;
        }

        var parteVariavel = ReajusteContrato.ParteVariavel(linhas);
        var baseReajustada = valorReajustado - parteVariavel;
        if (baseReajustada == 0 || valorReajustado == valorAnterior)
            return;

        var fator = (valorAnterior - parteVariavel) / baseReajustada;
        foreach (var linha in linhas.Where(l => parteVariavel == 0 || !l.ValorVariavel))
            linha.ValorUnitario = Math.Round(linha.ValorUnitario * fator, 2, MidpointRounding.AwayFromZero);
    }
}
