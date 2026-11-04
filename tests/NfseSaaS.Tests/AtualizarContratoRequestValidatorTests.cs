using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Edição das linhas de serviço de um Contrato existente: nulo = não mexe
/// nas linhas; informado = mesmas regras do cadastro.
/// </summary>
public class AtualizarContratoRequestValidatorTests
{
    private static readonly AtualizarContratoRequestValidator Validator = new();

    [Fact]
    public void Sem_linhas_de_servico_continua_valido_e_nao_altera_composicao()
    {
        var resultado = Validator.Validate(RequestValido() with { Servicos = null });

        Assert.True(resultado.IsValid, string.Join("; ", resultado.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Com_linhas_validas_deve_passar()
    {
        var resultado = Validator.Validate(RequestValido() with
        {
            Servicos = new[] { new ContratoServicoRequest(Guid.NewGuid(), 2, 150m), new ContratoServicoRequest(Guid.NewGuid(), 1, 99.90m) }
        });

        Assert.True(resultado.IsValid, string.Join("; ", resultado.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void Lista_vazia_deve_ser_reprovada()
    {
        var resultado = Validator.Validate(RequestValido() with { Servicos = Array.Empty<ContratoServicoRequest>() });

        Assert.Contains(resultado.Errors, e => e.ErrorMessage.Contains("ao menos um serviço"));
    }

    [Fact]
    public void Servico_repetido_deve_ser_reprovado()
    {
        var id = Guid.NewGuid();
        var resultado = Validator.Validate(RequestValido() with
        {
            Servicos = new[] { new ContratoServicoRequest(id, 1, 10m), new ContratoServicoRequest(id, 2, 10m) }
        });

        Assert.Contains(resultado.Errors, e => e.ErrorMessage.Contains("mais de uma linha"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    public void Quantidade_e_valor_devem_ser_maiores_que_zero(decimal quantidade, decimal valor)
    {
        var resultado = Validator.Validate(RequestValido() with
        {
            Servicos = new[] { new ContratoServicoRequest(Guid.NewGuid(), quantidade, valor) }
        });

        Assert.False(resultado.IsValid);
    }

    private static AtualizarContratoRequest RequestValido() => new(
        Descricao: "Manutenção do Sistema",
        DataInicioContrato: new DateOnly(2026, 1, 1),
        PeriodicidadeReajusteMeses: 12,
        IndiceReajuste: IndiceReajusteContrato.Ipca,
        DiasAlertaOverride: null,
        Observacao: null,
        Status: StatusContrato.Ativo,
        DataFim: null,
        TipoCobranca: TipoCobrancaContrato.Mensal,
        PermitirAlterarValorNaEmissao: false);
}
