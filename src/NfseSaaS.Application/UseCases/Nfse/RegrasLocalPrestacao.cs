namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// C2: local da prestação (cLocPrestacao) por nota. Ordem: o que veio na
/// nota → o padrão do Contrato → o município da Empresa (o que a DPS sempre
/// usou). O município escolhido tem que existir na tabela do IBGE (E0302).
///
/// Atenção fiscal: para os serviços em que o ISS é devido no local da
/// prestação (exceções do art. 3º da LC 116), o ISS passa a ser do município
/// escolhido. Para os demais (ex.: informática, 1.xx), muda só a informação.
/// </summary>
public static class RegrasLocalPrestacao
{
    public const string AvisoFiscal =
        "Para serviços em que o ISS é devido onde o serviço é prestado (ex.: limpeza, vigilância, mão de obra), o ISS vai para o município escolhido. Na dúvida, confirme com o contador.";

    /// <summary>Só dígitos; em branco = nulo (usa o padrão).</summary>
    public static string? Normalizar(string? codigo)
    {
        var digitos = new string((codigo ?? string.Empty).Where(char.IsDigit).ToArray());
        return digitos.Length == 0 ? null : digitos;
    }

    /// <summary>
    /// Município que vai na DPS: nota → contrato → empresa. Na nota, NULO =
    /// "não escolhi" (vale o contrato); em branco = "o da empresa", mesmo com
    /// contrato que tenha outro padrão (a tela manda em branco ao limpar o campo).
    /// </summary>
    public static string Resolver(string? daNota, string? doContrato, string daEmpresa) =>
        daNota is not null
            ? Normalizar(daNota) ?? daEmpresa
            : Normalizar(doContrato) ?? daEmpresa;

    /// <summary>Nulo = mesmo município da empresa (não precisa guardar à parte no contrato).</summary>
    public static string? ParaGuardar(string? codigo, string municipioEmpresa)
    {
        var normalizado = Normalizar(codigo);
        return normalizado == municipioEmpresa ? null : normalizado;
    }
}
