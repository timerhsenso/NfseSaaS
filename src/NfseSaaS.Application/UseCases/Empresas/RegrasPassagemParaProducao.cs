using System.Globalization;
using NfseSaaS.Application.UseCases.Certificados;

namespace NfseSaaS.Application.UseCases.Empresas;

/// <summary>
/// Quando uma Empresa pode passar de Homologação para Produção, sem banco
/// nem SEFIN (testável isolada):
/// - precisa ter certificado digital cadastrado, legível e com chave;
/// - o certificado precisa estar dentro da validade;
/// - o certificado precisa passar no teste de conexão com a SEFIN
///   Nacional de PRODUÇÃO, feito na hora, a cada tentativa (não vale um
///   teste antigo: o certificado pode ter sido trocado ou revogado).
/// Voltar para Homologação nunca é barrado.
/// </summary>
public static class RegrasPassagemParaProducao
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string Complemento = " A empresa continua em homologação.";

    /// <summary>
    /// Motivo para NÃO ir para produção só pelo status do certificado
    /// (nulo quando o certificado pode ser testado na SEFIN).
    /// </summary>
    public static string? BloqueioPeloCertificado(CertificadoStatusResponse status, DateTimeOffset agora)
    {
        if (!status.Existe)
            return "Cadastre o certificado digital (A1, arquivo .pfx) da empresa antes de colocar em produção: sem ele a SEFIN Nacional não aceita a nota. Use o botão Certificado na lista de empresas." + Complemento;

        if (status.ValidoAte is { } ate && agora > ate)
            return $"O certificado digital da empresa venceu em {ate.ToString("dd/MM/yyyy", PtBr)}. Envie um certificado válido em Certificado antes de colocar em produção." + Complemento;

        if (status.ValidoDe is { } de && agora < de)
            return $"O certificado digital da empresa só começa a valer em {de.ToString("dd/MM/yyyy", PtBr)}. Tente de novo a partir dessa data." + Complemento;

        if (!status.Valido)
            return "O certificado digital cadastrado não pode ser usado (sem chave privada ou arquivo com problema). Envie o certificado de novo em Certificado." + Complemento;

        return null;
    }

    /// <summary>Motivo para NÃO ir para produção pelo teste com a SEFIN de produção (nulo quando passou).</summary>
    public static string? BloqueioPeloTeste(TesteConexaoResponse teste) =>
        teste.Sucesso
            ? null
            : $"O teste do certificado com a SEFIN Nacional de produção falhou: {teste.Mensagem.Trim().TrimEnd('.')}. Confira o certificado em Certificado e tente de novo." + Complemento;
}
