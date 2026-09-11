using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Processa o lote SEQUENCIALMENTE, um Contrato/grupo por vez — nunca em
/// paralelo (mesmo certificado mTLS compartilhado por Empresa, e lote
/// mensal não tem urgência de milissegundos) e sem transação guarda-chuva
/// (cada Nfse já é sua própria unidade, mesma lógica de
/// EmitirNfseUseCase). Uma falha isolada NUNCA aborta o restante do lote
/// — cada item segue seu próprio destino e o resultado completo (sucesso
/// e falha lado a lado) volta numa lista só pro chamador decidir o que
/// reprocessar.
/// </summary>
public sealed class EmitirNotaMensalLoteUseCase : IEmitirNotaMensalLoteUseCase
{
    private readonly AppDbContext _db;
    private readonly IEmitirNfseUseCase _emitirNfse;

    public EmitirNotaMensalLoteUseCase(AppDbContext db, IEmitirNfseUseCase emitirNfse)
    {
        _db = db;
        _emitirNfse = emitirNfse;
    }

    public async Task<IReadOnlyList<ItemResultadoNotaMensalResponse>> ExecutarAsync(EmitirNotaMensalLoteRequest request, CancellationToken cancellationToken)
    {
        var resultados = new List<ItemResultadoNotaMensalResponse>();

        foreach (var item in request.Itens)
        {
            // Cada Contrato do lote é buscado/validado individualmente —
            // um Contrato inválido/excluído entre a listagem de
            // candidatos e a confirmação vira UM resultado com falha,
            // não interrompe os outros.
            var contrato = await _db.Contratos.AsNoTracking().FirstOrDefaultAsync(
                c => c.Id == item.ContratoId && c.EmpresaId == request.EmpresaId, cancellationToken);

            if (contrato is null)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(item.ContratoId, "—", "—", false, null, null, "Contrato não encontrado para esta Empresa."));
                continue;
            }

            if (contrato.TipoCobranca != TipoCobrancaContrato.Mensal || contrato.Status != StatusContrato.Ativo)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, "—", contrato.Descricao, false, null, null, "Contrato não é mais elegível (precisa ser Mensal e Ativo)."));
                continue;
            }

            var cliente = await _db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contrato.ClienteId, cancellationToken);
            var clienteNome = cliente?.Nome ?? "—";

            var linhas = await (
                from cs in _db.ContratoServicos.AsNoTracking()
                where cs.ContratoId == contrato.Id
                join srv in _db.Servicos.AsNoTracking() on cs.ServicoId equals srv.Id
                select new { cs.ServicoId, cs.Quantidade, cs.ValorUnitario, srv.Descricao, srv.CodigoTributacaoNacional })
                .ToListAsync(cancellationToken);

            if (linhas.Count == 0)
            {
                resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, contrato.Descricao, false, null, null, "Contrato sem serviços cadastrados."));
                continue;
            }

            // ValorTotalAjustado só tem efeito se o Contrato permitir —
            // escala cada linha proporcionalmente, mesma técnica de
            // RegistrarReajusteUseCase, sem alterar o Contrato (é só
            // pra esta emissão).
            var fator = 1m;
            if (item.ValorTotalAjustado.HasValue && contrato.PermitirAlterarValorNaEmissao && contrato.ValorAtual != 0)
                fator = item.ValorTotalAjustado.Value / contrato.ValorAtual;

            // Agrupa por CodigoTributacaoNacional — a NFS-e Nacional
            // aceita só 1 cServ por documento (ver DpsBuilder), então
            // serviços de código diferente NUNCA entram na mesma nota;
            // do mesmo código, viram 1 nota com descrição combinada.
            var grupos = linhas.GroupBy(l => l.CodigoTributacaoNacional);

            foreach (var grupo in grupos)
            {
                var primeiraLinha = grupo.First();
                var valorGrupo = Math.Round(grupo.Sum(l => l.Quantidade * l.ValorUnitario) * fator, 2);
                var descricao = grupo.Count() == 1
                    ? primeiraLinha.Descricao
                    : string.Join("; ", grupo.Select(l => l.Descricao).Distinct());

                // Determinística: mesmo Contrato+competência+código
                // tributário nunca gera 2 notas, mesmo se o usuário
                // clicar "Emitir" duas vezes sem perceber — backstop
                // real de idempotência (a checagem de
                // "JaEmitidoNestaCompetencia" na listagem é só UX).
                var idempotencyKey = $"notamensal-{contrato.Id:N}-{request.Competencia:yyyyMM}-{primeiraLinha.CodigoTributacaoNacional}";

                try
                {
                    var resultado = await _emitirNfse.ExecutarAsync(
                        new EmitirNfseRequest(
                            contrato.EmpresaId, contrato.ClienteId, primeiraLinha.ServicoId,
                            valorGrupo, descricao, request.Competencia, contrato.Id, idempotencyKey),
                        cancellationToken);

                    resultados.Add(new ItemResultadoNotaMensalResponse(
                        contrato.Id, clienteNome, descricao, resultado.Sucesso, resultado.NfseId, resultado.NumeroNfse,
                        resultado.Sucesso ? null : (resultado.MensagemErro ?? "Rejeitada pela SEFIN.")));
                }
                catch (Exception ex) when (ex is RegraNegocioException or RecursoNaoEncontradoException or NfseValidationException or NfseCertificateException or NfseApiException)
                {
                    // Falhas conhecidas do próprio domínio/integração —
                    // mensagem direta pro usuário.
                    resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, descricao, false, null, null, ex.Message));
                }
                catch (Exception ex)
                {
                    // Qualquer outra coisa (timeout de rede, etc.) —
                    // captura mesmo assim: um item que estoura aqui não
                    // pode derrubar o restante do lote.
                    resultados.Add(new ItemResultadoNotaMensalResponse(contrato.Id, clienteNome, descricao, false, null, null, $"Falha inesperada: {ex.Message}"));
                }
            }
        }

        return resultados;
    }
}
