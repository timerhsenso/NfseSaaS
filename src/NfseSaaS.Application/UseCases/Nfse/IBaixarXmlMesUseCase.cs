using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>ZIP com os XMLs do mês (para o contador).</summary>
public sealed record XmlMesResponse(byte[] ZipBytes, string NomeArquivoZip, int Quantidade);

/// <summary>
/// "XML do mês" da tela de notas: o XML oficial de todas as notas
/// autorizadas e canceladas de uma empresa num mês de competência, num ZIP.
/// Leve (o XML já está guardado; nada de PDF nem SEFIN), por isso sem
/// limite de vezes — é o uso do dia a dia; a exportação completa (D1) é
/// para portabilidade.
/// </summary>
public interface IBaixarXmlMesUseCase
{
    /// <param name="competencia">Qualquer dia do mês (só ano e mês contam).</param>
    /// <param name="ambiente">Nulo = o ambiente atual da empresa.</param>
    Task<XmlMesResponse> ExecutarAsync(Guid empresaId, DateOnly competencia, TipoAmbiente? ambiente, CancellationToken cancellationToken);
}

public static class RegrasXmlMes
{
    /// <summary>Acima disto o ZIP ficaria grande demais para montar na memória da requisição.</summary>
    public const int MaximoNotas = 5000;

    public static string NomeArquivo(string cnpj, DateOnly competencia, TipoAmbiente ambiente)
    {
        var doc = new string(cnpj.Where(char.IsDigit).ToArray());
        var sufixo = ambiente == TipoAmbiente.Producao ? "" : "-homologacao";
        return $"XML-NFSe-{(doc.Length > 0 ? doc : "empresa")}-{competencia:yyyy-MM}{sufixo}.zip";
    }
}
