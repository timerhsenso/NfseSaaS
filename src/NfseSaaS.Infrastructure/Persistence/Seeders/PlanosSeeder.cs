using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// Planos e vagas de fundador iniciais (claude/plano-comercial.md). Roda no
/// startup e é idempotente:
/// - planos: só quando o catálogo está VAZIO (banco novo). Depois disso o
///   catálogo é do operador (Plataforma › Planos): plano excluído não volta e
///   mudar um valor aqui NÃO altera o banco;
/// - vagas de fundador: cria o grupo que faltar (não há tela para excluir).
/// </summary>
public static class PlanosSeeder
{
    /// <summary>Data dos preços iniciais (antes de qualquer assinatura).</summary>
    public static readonly DateOnly VigenciaInicial = new(2026, 1, 1);

    private sealed record PlanoInicial(
        int Codigo, string Nome, string Descricao, PublicoPlano Publico, bool ParaEmpresa, bool ParaContador,
        int MaxEmpresas, int? NotasPorMes, int Ordem, decimal Preco, decimal PrecoFundador);

    private static readonly PlanoInicial[] Iniciais =
    [
        new(Planos.CodigoMico, "Mico", "Para o MEI ou a pequena empresa que emite poucas notas.",
            PublicoPlano.Empresas, true, false, 1, 15, 10, 29m, 19m),
        new(Planos.CodigoEmpresa, "Empresa", "Para quem tem até 3 empresas ou emite mais notas.",
            PublicoPlano.Empresas, true, true, 3, 100, 20, 59m, 39m),
        new(Planos.CodigoEscritorio10, "Escritório 10", "Para o contador que emite por até 10 clientes.",
            PublicoPlano.Escritorios, false, true, 10, null, 30, 129m, 89m),
        new(Planos.CodigoEscritorio25, "Escritório 25", "Para o escritório com até 25 clientes.",
            PublicoPlano.Escritorios, false, true, 25, null, 40, 279m, 199m),
        new(Planos.CodigoEscritorio50, "Escritório 50", "Para o escritório com até 50 clientes. Acima disso, fale com a gente.",
            PublicoPlano.Escritorios, false, true, 50, null, 50, 499m, 349m)
    ];

    private static readonly (PublicoPlano Publico, int Quantidade)[] VagasIniciais =
    [
        (PublicoPlano.Empresas, 20),
        (PublicoPlano.Escritorios, 10)
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var catalogoVazio = !await db.Planos.AnyAsync();
        foreach (var inicial in catalogoVazio ? Iniciais : [])
        {
            var plano = new Plano
            {
                Codigo = inicial.Codigo,
                Nome = inicial.Nome,
                Descricao = inicial.Descricao,
                Publico = inicial.Publico,
                OfertarParaEmpresa = inicial.ParaEmpresa,
                OfertarParaContador = inicial.ParaContador,
                MaxEmpresas = inicial.MaxEmpresas,
                NotasPorMes = inicial.NotasPorMes,
                AVenda = true,
                Ordem = inicial.Ordem
            };
            db.Planos.Add(plano);
            db.PrecosPlanos.Add(new PrecoPlano
            {
                PlanoId = plano.Id,
                PrecoMensal = inicial.Preco,
                PrecoFundador = inicial.PrecoFundador,
                VigenteDesde = VigenciaInicial,
                Motivo = "Preço inicial"
            });
        }

        var grupos = (await db.VagasFundador.Select(v => v.Publico).ToListAsync()).ToHashSet();
        foreach (var (publico, quantidade) in VagasIniciais.Where(v => !grupos.Contains(v.Publico)))
            db.VagasFundador.Add(new VagasFundador { Publico = publico, Quantidade = quantidade });

        await db.SaveChangesAsync();
    }
}
