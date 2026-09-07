using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Services;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Implementação real do caso de uso de cancelamento de NFS-e: só permite
/// cancelar uma Nfse com Status == Autorizada, monta o evento e101101 a
/// partir dos dados persistidos, assina, envia à SEFIN e atualiza o
/// Status para Cancelada em caso de sucesso. Se a SEFIN rejeitar (ou a
/// comunicação falhar), a Nfse permanece Autorizada — nunca marcamos como
/// cancelada algo que a SEFIN não confirmou. A TENTATIVA de cancelamento
/// fica registrada no AuditLog mesmo quando rejeitada (é uma "operação
/// realizada" do ponto de vista de compliance, mesmo sem mudar o Status).
/// </summary>
public sealed class CancelarNfseUseCase : ICancelarNfseUseCase
{
    private readonly AppDbContext _db;
    private readonly INfseNacionalService _nfseNacionalService;
    private readonly IAuditLogWriter _auditLogWriter;

    public CancelarNfseUseCase(AppDbContext db, INfseNacionalService nfseNacionalService, IAuditLogWriter auditLogWriter)
    {
        _db = db;
        _nfseNacionalService = nfseNacionalService;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<CancelarNfseResult> ExecutarAsync(Guid nfseId, CancelarNfseRequest request, CancellationToken cancellationToken)
    {
        var nfse = await _db.NotasFiscais.FirstOrDefaultAsync(n => n.Id == nfseId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Nfse {nfseId} não encontrada.");

        if (nfse.Status != NfseStatus.Autorizada)
            throw new RegraNegocioException($"Só é possível cancelar uma Nfse com status Autorizada (status atual: {nfse.Status}).");

        if (string.IsNullOrWhiteSpace(nfse.ChaveAcesso))
            throw new RegraNegocioException("Nfse Autorizada sem ChaveAcesso — dado inconsistente, não é possível cancelar.");

        var empresa = await _db.Empresas.FirstOrDefaultAsync(e => e.Id == nfse.EmpresaId, cancellationToken)
            ?? throw new RecursoNaoEncontradoException($"Empresa {nfse.EmpresaId} não encontrada.");

        var eventoRequest = new EventoCancelamentoRequest(
            ChaveAcesso: nfse.ChaveAcesso,
            CnpjAutor: empresa.Cnpj,
            CodigoMotivo: request.CodigoMotivo,
            Motivo: request.Motivo);

        var resposta = await _nfseNacionalService.CancelarAsync(eventoRequest, empresa.Id, cancellationToken);

        if (resposta.Sucesso)
        {
            nfse.Status = NfseStatus.Cancelada;

            _auditLogWriter.Registrar("CancelarNfse", "Nfse", nfse.Id, new { Sucesso = true, request.CodigoMotivo, request.Motivo });

            await _db.SaveChangesAsync(cancellationToken);

            return new CancelarNfseResult(nfse.Id, true, null, null);
        }

        // Cancelamento rejeitado pela SEFIN — Nfse continua Autorizada,
        // mas a tentativa é registrada no AuditLog.
        _auditLogWriter.Registrar("CancelarNfse", "Nfse", nfse.Id, new
        {
            Sucesso = false,
            request.CodigoMotivo,
            request.Motivo,
            CodigoErro = resposta.Erro?.Codigo,
            MensagemErro = resposta.Erro?.Descricao
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new CancelarNfseResult(
            nfse.Id,
            false,
            resposta.Erro?.Codigo,
            resposta.Erro?.Descricao);
    }
}
