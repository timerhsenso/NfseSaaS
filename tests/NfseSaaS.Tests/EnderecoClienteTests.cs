using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Options;
using NfseSaaS.Nacional.Validation;
using NfseSaaS.Tests.TestData;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// C3: endereço do tomador opcional para pessoa física (sem retenção e sem
/// IBS/CBS), obrigatório para CNPJ; complemento, telefone e e-mail na DPS.
/// </summary>
public class EnderecoClienteTests
{
    private const string Cpf = "52998224725";
    private const string Cnpj = "11222333000181";

    private static CadastrarClienteRequest Cadastro(string doc, bool comEndereco, bool retem = false) => comEndereco
        ? new CadastrarClienteRequest(Guid.NewGuid(), doc, "Nome", "Apelido", null, null, "2927408", "40010000", "Rua A", "10", null, "Centro", "BA", RetemIss: retem)
        : new CadastrarClienteRequest(Guid.NewGuid(), doc, "Nome", "Apelido", null, null, null, null, null, null, null, null, null, RetemIss: retem);

    private static List<string> Erros(CadastrarClienteRequest r) =>
        new CadastrarClienteRequestValidator().Validate(r).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void Pessoa_fisica_sem_retencao_pode_ficar_sem_endereco()
    {
        Assert.Empty(Erros(Cadastro(Cpf, comEndereco: false)));
        Assert.Empty(Erros(Cadastro(Cpf, comEndereco: true)));
    }

    [Fact]
    public void CNPJ_e_retencao_exigem_endereco()
    {
        Assert.Contains(EnderecoCliente.MotivoCnpj, Erros(Cadastro(Cnpj, comEndereco: false)));
        Assert.Contains(EnderecoCliente.MotivoRetencao, Erros(Cadastro(Cpf, comEndereco: false, retem: true)));
        Assert.Empty(Erros(Cadastro(Cnpj, comEndereco: true)));
    }

    [Fact]
    public void Endereco_pela_metade_nao_vale()
    {
        var r = Cadastro(Cpf, comEndereco: false) with { Cep = "40010000" };
        Assert.Contains(EnderecoCliente.MotivoIncompleto, Erros(r));

        var semNumero = Cadastro(Cnpj, comEndereco: true) with { Numero = "" };
        Assert.Contains(EnderecoCliente.MotivoIncompleto, Erros(semNumero));
    }

    [Fact]
    public void Edicao_valida_so_o_formato_do_endereco()
    {
        var v = new AtualizarClienteRequestValidator();
        var vazio = new AtualizarClienteRequest("Nome", "Apelido", null, null, null, null, null, null, null, null, null);
        Assert.True(v.Validate(vazio).IsValid);

        var cepRuim = vazio with { CodigoMunicipio = "2927408", Cep = "4001", Logradouro = "R", Numero = "1", Bairro = "B", Uf = "BA" };
        Assert.Contains(v.Validate(cepRuim).Errors, e => e.ErrorMessage.Contains("Cep"));
    }

    [Fact]
    public void Motivo_obrigatorio_considera_IBS_CBS()
    {
        Assert.Null(EnderecoCliente.MotivoObrigatorio(Cpf, retemIss: false));
        Assert.Equal(EnderecoCliente.MotivoIbsCbs, EnderecoCliente.MotivoObrigatorio(Cpf, retemIss: false, ibsCbs: true));
        Assert.Equal(EnderecoCliente.MotivoCnpj, EnderecoCliente.MotivoObrigatorio("12ABC34501DE35", retemIss: false));
        Assert.Equal(EnderecoCliente.MotivoCnpj, EnderecoCliente.MotivoObrigatorio("11.222.333/0001-81", retemIss: false));
    }

    // ---------- DPS ----------

    private static DpsRequest Base() => DpsRequestFactory.CriarRequestDeTeste() with { TpAmb = "2" };

    private static string ConstruirValidando(DpsRequest request)
    {
        var (xml, _) = DpsBuilderFactory.Criar(LeiauteDps.Nt009).Construir(request);
        var erros = DpsXsdValidator.Validar(xml, DpsXsdValidator.PastaProducaoRestrita20261020);
        Assert.True(erros.Count == 0, "DPS inválida no XSD:\n" + string.Join("\n", erros));
        return xml;
    }

    [Fact]
    public void Pessoa_fisica_sem_endereco_gera_DPS_sem_grupo_end()
    {
        var tomador = new TomadorDps(Cpf, "Fulano", "", "", "", "", "");
        var request = Base() with { Tomador = tomador };

        Assert.Empty(new DpsValidator().Validar(request));
        var xml = ConstruirValidando(request);
        Assert.DoesNotContain("<end>", xml);
        Assert.DoesNotContain("<endNac>", xml);
    }

    [Fact]
    public void CNPJ_sem_endereco_e_barrado_antes_da_SEFIN()
    {
        var request = Base() with { Tomador = new TomadorDps(Cnpj, "Empresa", "", "", "", "", "") };
        Assert.Contains(new DpsValidator().Validar(request), e => e.Contains("E0235"));
    }

    [Fact]
    public void Complemento_telefone_e_email_vao_na_DPS_na_ordem_do_XSD()
    {
        var request = Base() with
        {
            Tomador = Base().Tomador with { Complemento = "Sala 101", Telefone = "(71) 99999-0000", Email = "cliente@teste.com.br" }
        };

        var xml = ConstruirValidando(request);
        Assert.Contains("<nro>127</nro><xCpl>Sala 101</xCpl><xBairro>Centro</xBairro>", xml);
        Assert.Contains("</end><fone>71999990000</fone><email>cliente@teste.com.br</email>", xml);
    }

    [Fact]
    public void Telefone_e_email_fora_do_formato_nao_vao_na_DPS()
    {
        var request = Base() with
        {
            Tomador = Base().Tomador with { Telefone = "123", Email = new string('a', 75) + "@x.com.br" }
        };

        var xml = ConstruirValidando(request);
        Assert.DoesNotContain("<fone>", xml.Split("<toma>")[1].Split("</toma>")[0]);
        Assert.DoesNotContain("<email>", xml.Split("<toma>")[1].Split("</toma>")[0]);
    }
}
