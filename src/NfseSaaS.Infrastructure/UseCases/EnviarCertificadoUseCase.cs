using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Equivalente, pela tela web, ao que NfseSaaS.CertTool fazia por linha de
/// comando: valida o .pfx (abre com a senha informada, confirma chave
/// privada) e grava via CertificateFileStore — mesmo armazenamento
/// criptografado em disco (Data Protection + DPAPI), mesma convenção de
/// arquivo por Empresa. Diferente do CertTool, aqui um certificado
/// vencido é REJEITADO (não só um aviso em console que ninguém veria numa
/// tela web) — é melhor recusar de cara do que deixar salvar algo que vai
/// falhar na primeira emissão.
/// </summary>
public sealed class EnviarCertificadoUseCase : IEnviarCertificadoUseCase
{
    private readonly AppDbContext _db;
    private readonly CertificateFileStore _store;

    public EnviarCertificadoUseCase(AppDbContext db, CertificateFileStore store)
    {
        _db = db;
        _store = store;
    }

    public async Task<CertificadoStatusResponse> ExecutarAsync(Guid empresaId, byte[] pfxBytes, string senha, CancellationToken cancellationToken)
    {
        // O armazenamento de certificado é indexado só por empresaId (um
        // Guid) — sem esta checagem, qualquer usuário autenticado (de
        // QUALQUER tenant) poderia enviar/sobrescrever o certificado de
        // uma Empresa de outro tenant, bastando adivinhar/obter o Guid.
        // _db.Empresas já é filtrado pelo Global Query Filter do tenant
        // atual, então esta consulta só acha a Empresa se ela pertencer
        // ao tenant do usuário autenticado.
        var empresaExiste = await _db.Empresas.AnyAsync(e => e.Id == empresaId, cancellationToken);
        if (!empresaExiste)
            throw new RecursoNaoEncontradoException($"Empresa {empresaId} não encontrada.");

        if (pfxBytes.Length == 0)
            throw new RegraNegocioException("Arquivo de certificado (.pfx) vazio ou não enviado.");

        if (string.IsNullOrWhiteSpace(senha))
            throw new RegraNegocioException("Informe a senha do certificado.");

        X509Certificate2 certificadoTeste;
        try
        {
            certificadoTeste = new X509Certificate2(pfxBytes, senha, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            throw new RegraNegocioException("Não foi possível abrir o arquivo .pfx — senha incorreta ou arquivo corrompido/inválido.");
        }

        using (certificadoTeste)
        {
            if (!certificadoTeste.HasPrivateKey)
                throw new RegraNegocioException("O certificado enviado não possui chave privada — não pode ser usado para assinatura/mTLS.");

            if (DateTime.Now > certificadoTeste.NotAfter)
                throw new RegraNegocioException(
                    $"O certificado está vencido desde {certificadoTeste.NotAfter:dd/MM/yyyy} — envie um certificado válido.");

            if (DateTime.Now < certificadoTeste.NotBefore)
                throw new RegraNegocioException(
                    $"O certificado só é válido a partir de {certificadoTeste.NotBefore:dd/MM/yyyy}.");

            await _store.SalvarAsync(empresaId, pfxBytes, senha, cancellationToken);

            return new CertificadoStatusResponse(
                Existe: true,
                Subject: certificadoTeste.Subject,
                ValidoDe: certificadoTeste.NotBefore,
                ValidoAte: certificadoTeste.NotAfter,
                Valido: true);
        }
    }
}
