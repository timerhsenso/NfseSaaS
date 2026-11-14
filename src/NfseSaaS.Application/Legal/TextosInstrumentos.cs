using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NfseSaaS.Domain.Legal;

namespace NfseSaaS.Application.Legal;

/// <summary>Texto de um instrumento legal numa versão e o SHA-256 dele (em hexadecimal minúsculo), gravado em cada aceite.</summary>
public sealed record TextoInstrumento(TipoDocumentoLegal Tipo, string Versao, string Html, string Hash);

/// <summary>
/// JUR1: textos do DPA e das autorizações (A1 e emissão automática), um
/// arquivo HTML embutido por versão em Legal/Textos
/// (<c>dpa-1.0.html</c>). Versão publicada não se edita: o hash gravado no
/// aceite tem que continuar batendo com o texto. Mudou o conteúdo, crie o
/// arquivo da versão nova e aponte <see cref="DocumentosLegais.Versoes"/> para ela.
/// </summary>
public static class TextosInstrumentos
{
    private static readonly ConcurrentDictionary<(TipoDocumentoLegal, string), TextoInstrumento?> Cache = new();

    public static string NomeArquivo(TipoDocumentoLegal tipo, string versao) => tipo switch
    {
        TipoDocumentoLegal.Dpa => $"dpa-{versao}.html",
        TipoDocumentoLegal.AutorizacaoCertificado => $"autorizacao-certificado-{versao}.html",
        TipoDocumentoLegal.AutorizacaoEmissaoAutomatica => $"autorizacao-emissao-automatica-{versao}.html",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, "Termos e Política ficam nas views (Views/Legal).")
    };

    /// <summary>O texto da versão pedida; nulo se não existir.</summary>
    public static TextoInstrumento? Obter(TipoDocumentoLegal tipo, string versao) =>
        Cache.GetOrAdd((tipo, versao), chave =>
        {
            var recurso = $"NfseSaaS.Application.Legal.Textos.{NomeArquivo(chave.Item1, chave.Item2)}";
            using var stream = typeof(TextosInstrumentos).Assembly.GetManifestResourceStream(recurso);
            if (stream is null)
                return null;
            using var leitor = new StreamReader(stream, Encoding.UTF8);
            var html = Normalizar(leitor.ReadToEnd());
            return new TextoInstrumento(chave.Item1, chave.Item2, html, CalcularHash(html));
        });

    /// <summary>O texto obrigatório (erro se faltar o arquivo: é falha de publicação, não do usuário).</summary>
    public static TextoInstrumento ObterObrigatorio(TipoDocumentoLegal tipo, string versao) =>
        Obter(tipo, versao) ?? throw new InvalidOperationException($"Texto do instrumento {tipo} {versao} não encontrado em Legal/Textos.");

    /// <summary>Quebra de linha única (\n) e sem espaço nas pontas: o mesmo texto dá o mesmo hash no Windows e no Linux.</summary>
    public static string Normalizar(string html) => html.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    public static string CalcularHash(string textoNormalizado) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(textoNormalizado)));
}
