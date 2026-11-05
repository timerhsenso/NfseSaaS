using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NfseSaaS.Nacional.Exceptions;

namespace NfseSaaS.Infrastructure.Certificates;

/// <summary>
/// Lê e grava o certificado .pfx de cada Empresa em disco, criptografado
/// em repouso via ASP.NET Core Data Protection (as chaves do Data
/// Protection ficam protegidas por certificado ou DPAPI — ver
/// AddCertificateStorage e ProtecaoChavesOptions).
///
/// Cada Empresa tem um arquivo "{empresaId}.cert.json" contendo o .pfx e a
/// senha, cada um protegido separadamente. O arquivo nunca fica no
/// wwwroot nem é versionado.
/// </summary>
public sealed class CertificateFileStore
{
    private const string ProtectorPurpose = "NfseSaaS.Certificados.v1";
    private const string SufixoArquivo = ".cert.json";

    private readonly IDataProtector _protector;
    private readonly CertificateStorageOptions _options;

    public CertificateFileStore(IDataProtectionProvider dataProtectionProvider, IOptions<CertificateStorageOptions> options)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options.Value;
    }

    /// <summary>
    /// Criptografa e grava o .pfx e a senha de uma Empresa. Usado no
    /// cadastro/atualização do certificado (fluxo de UI ainda a
    /// implementar — Fase 2 de casos de uso).
    /// </summary>
    public async Task SalvarAsync(Guid empresaId, byte[] pfxBytes, string senha, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new NfseCertificateException("CertificateStorage:BasePath não configurado.");

        Directory.CreateDirectory(_options.BasePath);

        var envelope = new CertificadoEnvelope(
            PfxProtegidoBase64: Convert.ToBase64String(_protector.Protect(pfxBytes)),
            SenhaProtegida: _protector.Protect(senha));

        var json = JsonSerializer.Serialize(envelope);
        await File.WriteAllTextAsync(CaminhoArquivo(empresaId), json, cancellationToken);
    }

    /// <summary>
    /// Lê, descriptografa e carrega o certificado da Empresa informada.
    /// Por padrão (<paramref name="validarPeriodoDeValidade"/> = true, uso
    /// normal para mTLS/assinatura) valida chave privada e período de
    /// vigência, lançando se algo estiver errado. Chamadores que só
    /// querem EXIBIR o status do certificado (ex.: tela de configuração)
    /// devem passar false — assim um certificado vencido ainda é
    /// carregado (pra mostrar "vencido em tal data" ao usuário) em vez de
    /// lançar uma exceção que impediria a própria checagem de status.
    /// </summary>
    public async Task<X509Certificate2> CarregarAsync(Guid empresaId, CancellationToken cancellationToken, bool validarPeriodoDeValidade = true)
    {
        var caminho = CaminhoArquivo(empresaId);

        if (!File.Exists(caminho))
            throw new NfseCertificateException($"Certificado da empresa {empresaId} não encontrado em '{caminho}'.");

        CertificadoEnvelope? envelope;
        try
        {
            var json = await File.ReadAllTextAsync(caminho, cancellationToken);
            envelope = JsonSerializer.Deserialize<CertificadoEnvelope>(json);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException($"Falha ao ler o arquivo de certificado da empresa {empresaId}.", ex);
        }

        if (envelope is null)
            throw new NfseCertificateException($"Arquivo de certificado da empresa {empresaId} está corrompido.");

        byte[] pfxBytes;
        string senha;
        try
        {
            pfxBytes = _protector.Unprotect(Convert.FromBase64String(envelope.PfxProtegidoBase64));
            senha = _protector.Unprotect(envelope.SenhaProtegida);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException(
                $"Não foi possível descriptografar o certificado da empresa {empresaId} (chaves de proteção divergentes?).", ex);
        }

        X509Certificate2 certificado;
        try
        {
            // IMPORTANTE: EphemeralKeySet NÃO é usado aqui de propósito.
            // O Windows/SChannel (usado pelo SslStream nativo do .NET no
            // handshake mTLS) precisa que a chave privada esteja acessível
            // via um contêiner de chave persistido para autenticação de
            // cliente — com EphemeralKeySet o handshake falha com
            // "As credenciais fornecidas para o pacote não foram
            // reconhecidas" (SEC_E_UNKNOWN_CREDENTIALS). Esta é exatamente
            // a combinação de flags já validada na POC (emissão real aceita
            // pela SEFIN, HTTP 201).
            certificado = new X509Certificate2(
                pfxBytes,
                senha,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException(
                $"Falha ao carregar o certificado da empresa {empresaId} (senha incorreta ou arquivo inválido).", ex);
        }

        if (!certificado.HasPrivateKey)
            throw new NfseCertificateException($"O certificado da empresa {empresaId} não possui chave privada.");

        if (validarPeriodoDeValidade && (DateTime.Now < certificado.NotBefore || DateTime.Now > certificado.NotAfter))
            throw new NfseCertificateException(
                $"O certificado da empresa {empresaId} está fora do período de validade ({certificado.NotBefore:dd/MM/yyyy} a {certificado.NotAfter:dd/MM/yyyy}).");

        return certificado;
    }

    /// <summary>A Empresa tem certificado guardado (sem abrir nem descriptografar o arquivo).</summary>
    /// <summary>Apaga o certificado da Empresa (exclusão de conta). Sem arquivo: não faz nada.</summary>
    public void Excluir(Guid empresaId)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            return;
        var caminho = CaminhoArquivo(empresaId);
        if (File.Exists(caminho))
            File.Delete(caminho);
    }

    public bool ExisteCertificado(Guid empresaId) =>
        !string.IsNullOrWhiteSpace(_options.BasePath) && File.Exists(CaminhoArquivo(empresaId));

    /// <summary>Empresas que têm certificado guardado (arquivos "{empresaId}.cert.json" da pasta).</summary>
    public IReadOnlyList<Guid> ListarEmpresasComCertificado()
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath) || !Directory.Exists(_options.BasePath))
            return Array.Empty<Guid>();

        return Directory.EnumerateFiles(_options.BasePath, "*" + SufixoArquivo)
            .Select(caminho => Path.GetFileName(caminho)[..^SufixoArquivo.Length])
            .Select(nome => Guid.TryParseExact(nome, "N", out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();
    }

    /// <summary>
    /// Ids das chaves do Data Protection que protegem o arquivo da Empresa
    /// (o .pfx e a senha) — lidos do cabeçalho de cada conteúdo protegido,
    /// sem descriptografar. Usado para conferir, antes de apagar uma chave
    /// antiga, que nenhum certificado ainda depende dela.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> ChavesUsadasAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var envelope = await LerEnvelopeAsync(empresaId, cancellationToken);
        return new HashSet<Guid>
        {
            IdDaChave(Convert.FromBase64String(envelope.PfxProtegidoBase64)),
            IdDaChave(WebEncoders.Base64UrlDecode(envelope.SenhaProtegida))
        };
    }

    /// <summary>
    /// Regrava o arquivo da Empresa protegido com a chave ATUAL do Data
    /// Protection. Confere que o conteúdo novo abre igual ao antigo antes de
    /// substituir o arquivo, e a troca é atômica (grava num .tmp e renomeia).
    /// </summary>
    public async Task ReprotegerAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var envelope = await LerEnvelopeAsync(empresaId, cancellationToken);

        byte[] pfxBytes;
        string senha;
        try
        {
            pfxBytes = _protector.Unprotect(Convert.FromBase64String(envelope.PfxProtegidoBase64));
            senha = _protector.Unprotect(envelope.SenhaProtegida);
        }
        catch (Exception ex)
        {
            throw new NfseCertificateException(
                $"Não foi possível descriptografar o certificado da empresa {empresaId} para regravá-lo.", ex);
        }

        var novo = new CertificadoEnvelope(
            PfxProtegidoBase64: Convert.ToBase64String(_protector.Protect(pfxBytes)),
            SenhaProtegida: _protector.Protect(senha));

        if (!_protector.Unprotect(Convert.FromBase64String(novo.PfxProtegidoBase64)).AsSpan().SequenceEqual(pfxBytes)
            || _protector.Unprotect(novo.SenhaProtegida) != senha)
        {
            throw new NfseCertificateException($"Conferência falhou ao regravar o certificado da empresa {empresaId}; o arquivo original foi mantido.");
        }

        var caminho = CaminhoArquivo(empresaId);
        var temporario = caminho + ".tmp";
        await File.WriteAllTextAsync(temporario, JsonSerializer.Serialize(novo), cancellationToken);
        File.Move(temporario, caminho, overwrite: true);
    }

    /// <summary>
    /// Id da chave gravado no cabeçalho de um conteúdo protegido pelo Data
    /// Protection: 4 bytes do "magic header" (09 F0 C9 F0) e depois os 16
    /// bytes do Guid da chave.
    /// </summary>
    public static Guid IdDaChave(byte[] protegido)
    {
        if (protegido.Length < 20 || protegido[0] != 0x09 || protegido[1] != 0xF0 || protegido[2] != 0xC9 || protegido[3] != 0xF0)
            throw new NfseCertificateException("Conteúdo protegido em formato desconhecido (cabeçalho do Data Protection ausente).");

        return new Guid(protegido.AsSpan(4, 16));
    }

    private async Task<CertificadoEnvelope> LerEnvelopeAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var caminho = CaminhoArquivo(empresaId);
        if (!File.Exists(caminho))
            throw new NfseCertificateException($"Certificado da empresa {empresaId} não encontrado em '{caminho}'.");

        var json = await File.ReadAllTextAsync(caminho, cancellationToken);
        return JsonSerializer.Deserialize<CertificadoEnvelope>(json)
            ?? throw new NfseCertificateException($"Arquivo de certificado da empresa {empresaId} está corrompido.");
    }

    private string CaminhoArquivo(Guid empresaId) => Path.Combine(_options.BasePath, $"{empresaId:N}{SufixoArquivo}");

    private sealed record CertificadoEnvelope(string PfxProtegidoBase64, string SenhaProtegida);
}