using NfseSaaS.Domain.Entities;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Número do contrato: texto livre, proposta "ano/NNNN" e rótulo.</summary>
public class NumeroContratoRegrasTests
{
    [Fact]
    public void Proximo_continua_a_sequencia_do_ano_e_ignora_outros_formatos()
    {
        Assert.Equal("2026/0001", NumeroContratoRegras.Proximo(2026, Array.Empty<string?>()));
        Assert.Equal("2026/0008", NumeroContratoRegras.Proximo(2026, new[] { "2026/0007", "2026/0002", "2025/0099", "CT-15", null, " 2026 / 3 " }));
        Assert.Equal("2026/0001", NumeroContratoRegras.Proximo(2026, new[] { "2025/0010", "0042/A" }));
        Assert.Equal("2026/10000", NumeroContratoRegras.Proximo(2026, new[] { "2026/9999" }));
    }

    [Theory]
    [InlineData("  CT  15 ", "CT 15")]
    [InlineData("2026/0001", "2026/0001")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Normaliza_espacos(string? entrada, string? esperado)
    {
        Assert.Equal(esperado, NumeroContratoRegras.Normalizar(entrada));
    }

    [Fact]
    public void Rotulo_usa_numero_e_descricao_quando_houver()
    {
        Assert.Equal("Contrato 2026/0007 — Folha filial", NumeroContratoRegras.Rotulo("2026/0007", "Folha filial"));
        Assert.Equal("Contrato 2026/0007", NumeroContratoRegras.Rotulo("2026/0007", ""));
        Assert.Equal("Manutenção", NumeroContratoRegras.Rotulo(null, "Manutenção"));
        Assert.Equal("Contrato", NumeroContratoRegras.Rotulo(null, null));
    }
}
