using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>Regras do catálogo de planos: preço vigente por data e validações da tela Plataforma › Planos.</summary>
public class CatalogoPlanosRegrasTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 5);

    private static PrecoPlano Preco(decimal valor, DateOnly desde) =>
        new() { PrecoMensal = valor, PrecoFundador = valor - 10, VigenteDesde = desde };

    private static SalvarPlanoRequest PlanoValido(decimal? preco = 129, decimal? fundador = 89) =>
        new("Escritório 10", "Até 10 clientes", PublicoPlano.Escritorios, false, true, 10, null, true, 30, preco, fundador);

    [Fact]
    public void Preco_vigente_e_o_mais_recente_ate_hoje_e_ignora_o_agendado()
    {
        var antigo = Preco(29, new DateOnly(2026, 1, 1));
        var atual = Preco(35, Hoje);
        var agendado = Preco(39, Hoje.AddDays(30));
        PrecoPlano[] precos = [agendado, antigo, atual];

        Assert.Same(atual, RegrasCatalogoPlanos.PrecoVigente(precos, Hoje));
        Assert.Same(antigo, RegrasCatalogoPlanos.PrecoVigente(precos, Hoje.AddDays(-1)));
        Assert.Same(agendado, RegrasCatalogoPlanos.PrecoVigente(precos, Hoje.AddDays(30)));
        Assert.Null(RegrasCatalogoPlanos.PrecoVigente(precos, new DateOnly(2025, 12, 31)));

        Assert.Same(agendado, RegrasCatalogoPlanos.ProximoPreco(precos, Hoje));
        Assert.Null(RegrasCatalogoPlanos.ProximoPreco(precos, Hoje.AddDays(30)));

        Assert.Equal("Vigente", RegrasCatalogoPlanos.Situacao(atual, atual, Hoje));
        Assert.Equal("Anterior", RegrasCatalogoPlanos.Situacao(antigo, atual, Hoje));
        Assert.Equal("Agendado", RegrasCatalogoPlanos.Situacao(agendado, atual, Hoje));
    }

    [Fact]
    public void Plano_valido_passa_e_os_erros_sao_explicados()
    {
        RegrasCatalogoPlanos.ValidarPlano(PlanoValido(), criacao: true);
        RegrasCatalogoPlanos.ValidarPlano(PlanoValido(null, null), criacao: false); // preço só na criação

        Assert.Contains("preço normal e o de fundador",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido(null, null), criacao: true)).Message);
        Assert.Contains("não pode ser maior",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido(89, 129), criacao: true)).Message);
        Assert.Contains("2 casas",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido(129.999m, 89), criacao: true)).Message);
        Assert.Contains("Nome",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { Nome = " " }, criacao: true)).Message);
        Assert.Contains("Empresas incluídas",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { MaxEmpresas = 0 }, criacao: true)).Message);
        Assert.Contains("Notas por mês",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { NotasPorMes = 0 }, criacao: true)).Message);
        Assert.Contains("aparecer para",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { OfertarParaContador = false }, criacao: true)).Message);
        Assert.Contains("Grupo",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { Publico = (PublicoPlano)9 }, criacao: true)).Message);

        // Fora de venda pode ficar sem público (só os assinantes atuais o usam).
        RegrasCatalogoPlanos.ValidarPlano(PlanoValido() with { AVenda = false, OfertarParaContador = false }, criacao: true);
    }

    [Fact]
    public void Novo_preco_nao_vale_para_tras_e_vagas_tem_limite()
    {
        RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(39, 29, Hoje, "reajuste"), Hoje);
        RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(39, 39, Hoje.AddDays(10), null), Hoje);

        Assert.Contains("passada",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(39, 29, Hoje.AddDays(-1), null), Hoje)).Message);
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(0, 0, Hoje, null), Hoje));

        // Mínimo de cobrança do Asaas: R$ 5,00 (normal e fundador).
        Assert.Contains("R$ 5,00",
            Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(9.99m, 1.99m, Hoje, null), Hoje)).Message);
        RegrasCatalogoPlanos.ValidarNovoPreco(new NovoPrecoPlanoRequest(5m, 5m, Hoje, null), Hoje);

        RegrasCatalogoPlanos.ValidarVagas(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, 0, null));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarVagas(new AlterarVagasFundadorRequest(PublicoPlano.Empresas, -1, null)));
        Assert.Throws<RegraNegocioException>(() => RegrasCatalogoPlanos.ValidarVagas(new AlterarVagasFundadorRequest((PublicoPlano)0, 5, null)));
    }

    [Fact]
    public void So_exclui_plano_que_nunca_foi_assinado()
    {
        Assert.Null(RegrasCatalogoPlanos.MotivoNaoPodeExcluir(0));
        Assert.Contains("já foi assinado", RegrasCatalogoPlanos.MotivoNaoPodeExcluir(1)!);
    }
}
