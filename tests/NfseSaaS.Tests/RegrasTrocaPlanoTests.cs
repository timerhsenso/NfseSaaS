using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using Xunit;

namespace NfseSaaS.Tests;

public class RegrasTrocaPlanoTests
{
    private static PlanoVigente Plano(int codigo, string nome, PublicoPlano publico, int maxEmpresas, decimal preco, decimal fundador) =>
        new(codigo, nome, "", publico, true, true, maxEmpresas, null, true, preco, fundador);

    private static readonly PlanoVigente Mico = Plano(1, "Mico", PublicoPlano.Empresas, 1, 29m, 19m);
    private static readonly PlanoVigente Empresa = Plano(2, "Empresa", PublicoPlano.Empresas, 3, 59m, 39m);
    private static readonly PlanoVigente Escritorio10 = Plano(3, "Escritório 10", PublicoPlano.Escritorios, 10, 129m, 89m);

    [Fact]
    public void Fundador_que_sobe_no_mesmo_grupo_continua_fundador_mesmo_sem_vagas()
    {
        var troca = RegrasTrocaPlano.Calcular(Mico, fundadorAtual: true, Empresa, vagasFundadorNoGrupoNovo: 0, empresasAtivas: 1);
        Assert.Equal(new TrocaPlanoCalculada(39m, true), troca);
    }

    [Fact]
    public void Quem_nao_e_fundador_no_mesmo_grupo_paga_o_preco_cheio_mesmo_com_vagas()
    {
        var troca = RegrasTrocaPlano.Calcular(Mico, fundadorAtual: false, Empresa, vagasFundadorNoGrupoNovo: 5, empresasAtivas: 1);
        Assert.Equal(new TrocaPlanoCalculada(59m, false), troca);
    }

    [Theory]
    [InlineData(true, 3, 89, true)]
    [InlineData(true, 0, 129, false)]
    [InlineData(false, 2, 89, true)]
    public void Mudar_de_grupo_segue_as_vagas_do_grupo_novo(bool fundadorAtual, int vagas, decimal preco, bool fundador)
    {
        var troca = RegrasTrocaPlano.Calcular(Empresa, fundadorAtual, Escritorio10, vagas, empresasAtivas: 2);
        Assert.Equal(new TrocaPlanoCalculada(preco, fundador), troca);
    }

    [Fact]
    public void Nao_desce_para_plano_com_menos_empresas_que_as_ativas()
    {
        var erro = Assert.Throws<RegraNegocioException>(() =>
            RegrasTrocaPlano.Calcular(Empresa, true, Mico, 0, empresasAtivas: 2));
        Assert.Contains("2 empresa(s) ativa(s)", erro.Message);
        Assert.Contains("até 1", erro.Message);
    }

    [Fact]
    public void Nao_troca_para_o_mesmo_plano()
    {
        Assert.Throws<RegraNegocioException>(() => RegrasTrocaPlano.Calcular(Mico, true, Mico, 5, 1));
    }

    [Fact]
    public void Descricao_mostra_de_e_para()
    {
        var texto = RegrasTrocaPlano.Descrever("Mico", 19m, "Empresa", 39m);
        Assert.Contains("Mico", texto);
        Assert.Contains("19,00", texto);
        Assert.Contains("→ Empresa", texto);
        Assert.Contains("39,00", texto);
    }
}
