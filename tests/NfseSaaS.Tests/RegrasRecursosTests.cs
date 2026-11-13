using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Domain.Enums;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>PL1: catálogo de recursos e a ordem exceção → plano → padrão.</summary>
public class RegrasRecursosTests
{
    private static RecursoPlano Logo => CatalogoRecursos.ObterOuErro(CatalogoRecursos.LogoPropria);
    private static RecursoPlano Clientes => CatalogoRecursos.ObterOuErro(CatalogoRecursos.ClientesPorEmpresa);

    [Fact]
    public void Catalogo_tem_codigos_unicos_e_todos_achaveis()
    {
        Assert.Equal(CatalogoRecursos.Todos.Count, CatalogoRecursos.Todos.Select(r => r.Codigo).Distinct().Count());
        Assert.All(CatalogoRecursos.Todos, r => Assert.Same(r, CatalogoRecursos.Obter(r.Codigo)));
        Assert.Throws<RegraNegocioException>(() => CatalogoRecursos.ObterOuErro("nao-existe"));
    }

    [Fact]
    public void Padrao_reproduz_o_comportamento_de_antes()
    {
        // Logo e rodapé: só contas de contador; limites: ilimitados.
        Assert.True(RegrasRecursos.Resolver(Logo, PerfilConta.Contador, null, null).Ligado);
        Assert.False(RegrasRecursos.Resolver(Logo, PerfilConta.Empresa, null, null).Ligado);
        var clientes = RegrasRecursos.Resolver(Clientes, PerfilConta.Empresa, null, null);
        Assert.Null(clientes.Limite);
        Assert.Equal(OrigemRecurso.Padrao, clientes.Origem);
        Assert.Equal("Ilimitado", clientes.Texto);
    }

    [Fact]
    public void Plano_vence_o_padrao_e_excecao_vence_o_plano()
    {
        var doPlano = RegrasRecursos.Resolver(Logo, PerfilConta.Empresa, new ValorRecurso(true, null), null);
        Assert.True(doPlano.Ligado);
        Assert.Equal(OrigemRecurso.Plano, doPlano.Origem);

        var excecao = RegrasRecursos.Resolver(Logo, PerfilConta.Empresa, new ValorRecurso(true, null), new ValorRecurso(false, null), new DateOnly(2026, 12, 31));
        Assert.False(excecao.Ligado);
        Assert.Equal(OrigemRecurso.Excecao, excecao.Origem);
        Assert.Equal(new DateOnly(2026, 12, 31), excecao.ExcecaoValidaAte);

        var limite = RegrasRecursos.Resolver(Clientes, PerfilConta.Empresa, new ValorRecurso(null, 20), null);
        Assert.Equal(20, limite.Limite);
        Assert.Equal("Até 20", limite.Texto);
    }

    [Fact]
    public void Excecao_vale_ate_o_ultimo_dia_inclusive()
    {
        var hoje = new DateOnly(2026, 11, 13);
        Assert.True(RegrasRecursos.ExcecaoVale(null, hoje));
        Assert.True(RegrasRecursos.ExcecaoVale(hoje, hoje));
        Assert.False(RegrasRecursos.ExcecaoVale(hoje.AddDays(-1), hoje));
    }

    [Fact]
    public void Normalizar_confere_o_valor_pelo_tipo()
    {
        Assert.Equal(new ValorRecurso(true, null), RegrasRecursos.Normalizar(Logo, true, 5));
        Assert.Throws<RegraNegocioException>(() => RegrasRecursos.Normalizar(Logo, null, null));
        Assert.Equal(new ValorRecurso(null, 10), RegrasRecursos.Normalizar(Clientes, true, 10));
        Assert.Equal(new ValorRecurso(null, null), RegrasRecursos.Normalizar(Clientes, null, null));
        Assert.Throws<RegraNegocioException>(() => RegrasRecursos.Normalizar(Clientes, null, -1));
    }

    [Theory]
    [InlineData(null, 1000, true)]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(0, 0, false)]
    public void Cabe_mais_um(int? limite, int atual, bool cabe) => Assert.Equal(cabe, RegrasRecursos.CabeMaisUm(limite, atual));

    [Fact]
    public void Motivo_da_excecao_e_obrigatorio()
    {
        Assert.Throws<RegraNegocioException>(() => RegrasRecursos.ValidarMotivo("curto"));
        Assert.Equal("Cortesia de lançamento", RegrasRecursos.ValidarMotivo("  Cortesia de lançamento "));
    }

    [Fact]
    public void Mensagem_de_limite_diz_o_que_fazer()
    {
        var r = RegrasRecursos.Resolver(Clientes, PerfilConta.Empresa, new ValorRecurso(null, 3), null);
        var msg = RegrasRecursos.MensagemLimite(r, 3);
        Assert.Contains("até 3 clientes por empresa", msg);
        Assert.Contains("Assinatura", msg);
    }
}
