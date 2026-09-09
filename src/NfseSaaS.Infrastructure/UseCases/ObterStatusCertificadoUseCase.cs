using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.UseCases;

public sealed class ObterStatusCertificadoUseCase : IObterStatusCertificadoUseCase
{
    private readonly AppDbContext _db;
    private readonly CertificateFileStore _store;

    public ObterStatusCertificadoUseCase(AppDbContext db, CertificateFileStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<CertificadoStatusResponse> ExecutarAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        // Ver comentário completo em EnviarCertificadoUseCase — mesma
        // checagem obrigatória de tenant via _db.Empresas.
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        try
        {
            // validarPeriodoDeValidade: false — precisamos mostrar o
            // status de um certificado VENCIDO também, não só lançar.
            using var certificado = await _store.CarregarAsync(empresaId, cancellationToken, validarPeriodoDeValidade: false);

            var dentroDaValidade = DateTime.Now >= certificado.NotBefore && DateTime.Now <= certificado.NotAfter;

            return new CertificadoStatusResponse(
                Existe: true,
                Subject: certificado.Subject,
                ValidoDe: certificado.NotBefore,
                ValidoAte: certificado.NotAfter,
                Valido: dentroDaValidade && certificado.HasPrivateKey);
        }
        catch (NfseCertificateException)
        {
            // Arquivo não existe, corrompido, ou senha/chave de proteção
            // divergente — do ponto de vista da tela, tudo isso é
            // "sem certificado utilizável" (Existe = false). O motivo
            // exato já foi pro log pelo ExceptionHandlingMiddleware se
            // isto tivesse propagado — aqui é status, não erro fatal.
            return new CertificadoStatusResponse(Existe: false, Subject: null, ValidoDe: null, ValidoAte: null, Valido: false);
        }
    }
}
