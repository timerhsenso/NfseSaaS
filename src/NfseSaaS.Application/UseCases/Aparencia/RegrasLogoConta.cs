using System.Security.Cryptography;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Aparencia;

/// <summary>
/// Regras do logo da conta (B9). Só PNG, JPEG e WebP, reconhecidos pelos
/// primeiros bytes do arquivo (a extensão não vale nada). SVG fica de fora
/// de propósito: é texto que pode levar script e seria servido pelo nosso
/// domínio.
/// </summary>
public static class RegrasLogoConta
{
    public const int TamanhoMaximoBytes = 512 * 1024;

    /// <summary>
    /// Logo é recurso do plano Escritório: contas de contador. O operador da
    /// plataforma também pode (para conferir a tela na própria conta).
    /// </summary>
    public static bool Disponivel(PerfilConta perfil, bool operadorPlataforma) =>
        perfil == PerfilConta.Contador || operadorPlataforma;

    public const string MensagemIndisponivel =
        "O logo próprio faz parte do plano Escritório (contas de contador).";

    /// <summary>Tipo da imagem pelo conteúdo; null se não for PNG, JPEG ou WebP.</summary>
    public static string? DetectarTipo(ReadOnlySpan<byte> conteudo)
    {
        if (conteudo.Length >= 8 && conteudo[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (conteudo.Length >= 3 && conteudo[0] == 0xFF && conteudo[1] == 0xD8 && conteudo[2] == 0xFF)
            return "image/jpeg";
        if (conteudo.Length >= 12
            && conteudo[..4].SequenceEqual("RIFF"u8)
            && conteudo.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    /// <summary>Confere o arquivo enviado e devolve o tipo; erro com mensagem para a tela.</summary>
    public static string Validar(byte[]? conteudo)
    {
        if (conteudo is null || conteudo.Length == 0)
            throw new RegraNegocioException("Escolha a imagem do logo.");
        if (conteudo.Length > TamanhoMaximoBytes)
            throw new RegraNegocioException($"O logo pode ter até {TamanhoMaximoBytes / 1024} KB. Reduza a imagem e envie de novo.");
        return DetectarTipo(conteudo)
            ?? throw new RegraNegocioException("Envie o logo em PNG, JPG ou WebP.");
    }

    /// <summary>Versão do conteúdo (16 primeiros dígitos do SHA-256) — vai na URL da imagem.</summary>
    public static string Versao(byte[] conteudo) =>
        Convert.ToHexString(SHA256.HashData(conteudo))[..16].ToLowerInvariant();
}
