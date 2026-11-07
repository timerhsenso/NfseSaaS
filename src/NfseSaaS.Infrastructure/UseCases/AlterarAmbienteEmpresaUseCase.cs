using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Troca o ambiente (Homologação ⇄ Produção) de uma Empresa — nos DOIS
/// sentidos (revisão da versão anterior, que só permitia promover pra
/// Produção sem volta). Continua restrito a Administrador e auditado, nos
/// dois sentidos: mudar PARA Produção decide se a próxima nota tem
/// efeito fiscal real; voltar PARA Homologação é igualmente uma decisão
/// deliberada (deixa de emitir notas reais), então merece o mesmo
/// rastro.
///
/// Idempotente: se o ambiente pedido já é o atual, não faz nada e não
/// gera entrada de auditoria.
///
/// NÃO reprocessa nem tenta "corrigir" notas já emitidas — cada Nfse
/// já existente mantém o TipoAmbiente que tinha no momento em que foi
/// emitida (ver comentário em Nfse.TipoAmbiente). Trocar o ambiente da
/// Empresa só afeta o que ela emite/lista DAQUI PRA FRENTE.
///
/// Ir PARA Produção exige certificado digital válido e testado na hora
/// contra a SEFIN Nacional de produção (ver RegrasPassagemParaProducao):
/// a cada tentativa, nunca um teste antigo. Falhou, a Empresa fica em
/// Homologação e a tela mostra o motivo.
/// </summary>
public sealed class AlterarAmbienteEmpresaUseCase : IAlterarAmbienteEmpresaUseCase
{
    private readonly AppDbContext _db;
    private readonly IAuditLogWriter _auditLogWriter;
    private readonly IObterStatusCertificadoUseCase _statusCertificado;
    private readonly ITestarConexaoCertificadoUseCase _testarConexao;

    public AlterarAmbienteEmpresaUseCase(
        AppDbContext db,
        IAuditLogWriter auditLogWriter,
        IObterStatusCertificadoUseCase statusCertificado,
        ITestarConexaoCertificadoUseCase testarConexao)
    {
        _db = db;
        _auditLogWriter = auditLogWriter;
        _statusCertificado = statusCertificado;
        _testarConexao = testarConexao;
    }

    public async Task ExecutarAsync(Guid id, TipoAmbiente novoAmbiente, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        if (empresa is null)
            throw new RecursoNaoEncontradoException($"Empresa {id} não encontrada.");

        if (empresa.TipoAmbiente == novoAmbiente)
            return;

        string? certificadoTestado = null;
        if (novoAmbiente == TipoAmbiente.Producao)
            certificadoTestado = await ExigirCertificadoTestadoEmProducaoAsync(id, cancellationToken);

        var ambienteAnterior = empresa.TipoAmbiente;
        empresa.TipoAmbiente = novoAmbiente;

        _auditLogWriter.Registrar(
            "AlterarAmbienteEmpresa",
            "Empresa",
            empresa.Id,
            new { empresa.RazaoSocial, empresa.Cnpj, AmbienteAnterior = ambienteAnterior.ToString(), AmbienteNovo = novoAmbiente.ToString(), CertificadoTestado = certificadoTestado });

        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Certificado válido e aceito pela SEFIN de produção agora; senão, 422 com o motivo.</summary>
    /// <returns>O Subject do certificado testado (vai para a auditoria).</returns>
    private async Task<string?> ExigirCertificadoTestadoEmProducaoAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var status = await _statusCertificado.ExecutarAsync(empresaId, cancellationToken);
        var bloqueio = RegrasPassagemParaProducao.BloqueioPeloCertificado(status, DateTimeOffset.Now);
        if (bloqueio is not null)
            throw new RegraNegocioException(bloqueio);

        var teste = await _testarConexao.ExecutarAsync(empresaId, TipoAmbiente.Producao, cancellationToken);
        bloqueio = RegrasPassagemParaProducao.BloqueioPeloTeste(teste);
        if (bloqueio is not null)
            throw new RegraNegocioException(bloqueio);

        return status.Subject;
    }
}
