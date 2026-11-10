namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Município brasileiro pelo código IBGE (C9) — o cMun da DPS. Entidade
/// GLOBAL (não é de conta) e só leitura pela aplicação: alimentada pelo
/// MunicipioSeeder no startup a partir da tabela embarcada (IBGE).
/// </summary>
public sealed class Municipio
{
    /// <summary>Código IBGE de 7 dígitos (chave).</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>Sigla da UF (os 2 primeiros dígitos do código dizem qual é).</summary>
    public string Uf { get; set; } = string.Empty;

    /// <summary>Nome sem acento e em minúsculas, para a busca ("sao paulo" acha "São Paulo").</summary>
    public string NomeBusca { get; set; } = string.Empty;
}
