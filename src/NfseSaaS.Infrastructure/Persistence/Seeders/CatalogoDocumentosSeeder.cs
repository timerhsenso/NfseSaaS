using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Infrastructure.Persistence.Seeders;

/// <summary>
/// DOC4: o que o sistema traz no catálogo de documentos.
///
/// - <see cref="Sistema"/>: os 8 tipos de antes do catálogo e o "Outro
///   documento", com os números já gravados em documentos_empresa.Tipo.
///   Entram pela migration (HasData em TipoDocumentoConfiguration), porque a
///   chave estrangeira de documentos_empresa precisa deles antes.
/// - <see cref="Novos"/> e <see cref="Pacotes"/>: entram pelo seeder no
///   startup, pelo Codigo, só quando faltam. Nunca atualiza nem apaga: depois
///   de criado, quem manda é o operador (Plataforma › Documentos).
/// </summary>
public static class CatalogoDocumentosSeeder
{
    private static TipoDocumento Tipo(int id, string codigo, string nome, CategoriaDocumento categoria, AbrangenciaDocumento abrangencia,
        string? orgao, string? link, int? validadeDias, bool exigeValidade, bool padrao, int ordem) => new()
    {
        Id = id,
        Codigo = codigo,
        Nome = nome,
        Categoria = categoria,
        Abrangencia = abrangencia,
        Orgao = orgao,
        LinkEmissao = link,
        ValidadeDias = validadeDias,
        ExigeValidade = exigeValidade,
        Ativo = true,
        PadraoNovasEmpresas = padrao,
        Ordem = ordem,
        CreatedAt = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero)
    };

    /// <summary>Tipos com número fixo (migration). Prazos usuais: federal e CNDT 180 dias, CRF do FGTS 30.</summary>
    public static IReadOnlyList<TipoDocumento> Sistema() => new[]
    {
        Tipo(TiposDocumentoSistema.CndFederal, "cnd-federal", "Certidão federal (Receita/PGFN)", CategoriaDocumento.Fiscal, AbrangenciaDocumento.Nacional,
            "Receita Federal", "https://servicos.receitafederal.gov.br/servico/certidoes/#/home", 180, true, true, 10),
        Tipo(TiposDocumentoSistema.CndEstadual, "cnd-estadual", "Certidão estadual", CategoriaDocumento.Fiscal, AbrangenciaDocumento.Estadual,
            "SEFAZ do estado", null, null, true, true, 20),
        Tipo(TiposDocumentoSistema.CndMunicipal, "cnd-municipal", "Certidão municipal", CategoriaDocumento.Fiscal, AbrangenciaDocumento.Municipal,
            "Prefeitura", null, null, true, true, 30),
        Tipo(TiposDocumentoSistema.CrfFgts, "crf-fgts", "Regularidade do FGTS (CRF)", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional,
            "Caixa", "https://consulta-crf.caixa.gov.br/consultacrf/pages/consultaEmpregador.jsf", 30, true, true, 10),
        Tipo(TiposDocumentoSistema.Cndt, "cndt", "Certidão trabalhista (CNDT)", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional,
            "TST", "https://cndt-certidao.tst.jus.br/", 180, true, true, 20),
        Tipo(TiposDocumentoSistema.SimplesNacional, "simples-nacional", "Opção pelo Simples Nacional", CategoriaDocumento.Fiscal, AbrangenciaDocumento.Nacional,
            "Receita Federal", "https://www8.receita.fazenda.gov.br/SimplesNacional/aplicacoes.aspx?id=21", null, false, true, 40),
        Tipo(TiposDocumentoSistema.ContratoSocial, "contrato-social", "Contrato social", CategoriaDocumento.Cadastral, AbrangenciaDocumento.Estadual,
            "Junta Comercial", null, null, false, true, 20),
        Tipo(TiposDocumentoSistema.CartaoCnpj, "cartao-cnpj", "Cartão CNPJ", CategoriaDocumento.Cadastral, AbrangenciaDocumento.Nacional,
            "Receita Federal", "https://solucoes.receita.fazenda.gov.br/Servicos/cnpjreva/Cnpjreva_Solicitacao.asp", null, false, true, 10),
        Tipo(TiposDocumentoSistema.Outro, "outro", "Outro documento", CategoriaDocumento.Outros, AbrangenciaDocumento.Nacional,
            null, null, null, false, false, 999)
    };

    private sealed record NovoTipo(string Codigo, string Nome, CategoriaDocumento Categoria, AbrangenciaDocumento Abrangencia,
        string? Orgao, int? ValidadeDias, bool ExigeValidade, int Ordem);

    /// <summary>Tipos que o seeder cria (Id do banco, a partir de 1000). Sem link: estes variam por lugar ou não têm site único.</summary>
    private static readonly NovoTipo[] Novos =
    {
        new("inscricao-municipal", "Inscrição municipal (CCM)", CategoriaDocumento.Cadastral, AbrangenciaDocumento.Municipal, "Prefeitura", null, false, 30),
        new("inscricao-estadual", "Inscrição estadual", CategoriaDocumento.Cadastral, AbrangenciaDocumento.Estadual, "SEFAZ do estado", null, false, 40),
        new("alvara-funcionamento", "Alvará de funcionamento", CategoriaDocumento.Cadastral, AbrangenciaDocumento.Municipal, "Prefeitura", null, false, 50),
        new("fgts-pago-mes", "Comprovante do FGTS pago no mês", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional, "Caixa / FGTS Digital", null, false, 30),
        new("inss-pago-mes", "Comprovante do INSS pago no mês (DCTFWeb)", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional, "Receita Federal", null, false, 40),
        new("folha-pagamento", "Folha de pagamento do mês", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional, null, null, false, 50),
        new("seguro-vida", "Apólice de seguro de vida em grupo", CategoriaDocumento.Trabalhista, AbrangenciaDocumento.Nacional, "Seguradora", 365, false, 60),
        new("balanco-patrimonial", "Balanço patrimonial", CategoriaDocumento.FinanceiroContabil, AbrangenciaDocumento.Nacional, "Contador", null, false, 10),
        new("dre", "Demonstração do resultado (DRE)", CategoriaDocumento.FinanceiroContabil, AbrangenciaDocumento.Nacional, "Contador", null, false, 20),
        new("pgr", "PGR — Programa de Gerenciamento de Riscos", CategoriaDocumento.SegurancaTrabalho, AbrangenciaDocumento.Nacional, null, 730, false, 10),
        new("ltcat", "LTCAT — Laudo técnico das condições ambientais", CategoriaDocumento.SegurancaTrabalho, AbrangenciaDocumento.Nacional, null, null, false, 20),
        new("avcb", "AVCB — Auto de vistoria do Corpo de Bombeiros", CategoriaDocumento.SegurancaTrabalho, AbrangenciaDocumento.Estadual, "Corpo de Bombeiros", null, false, 30),
        new("pcmso", "PCMSO — Programa de Controle Médico de Saúde Ocupacional", CategoriaDocumento.SaudeOcupacional, AbrangenciaDocumento.Nacional, null, 365, false, 10),
        new("cno", "CNO — Cadastro Nacional de Obras", CategoriaDocumento.Tecnico, AbrangenciaDocumento.Nacional, "Receita Federal", null, false, 10),
        new("art", "ART — Anotação de Responsabilidade Técnica", CategoriaDocumento.Tecnico, AbrangenciaDocumento.Estadual, "CREA", null, false, 20),
        new("registro-conselho", "Registro no conselho de classe (CREA, CRM, CRC...)", CategoriaDocumento.Tecnico, AbrangenciaDocumento.Estadual, "Conselho de classe", null, false, 30),
        new("atestado-capacidade", "Atestado de capacidade técnica", CategoriaDocumento.Tecnico, AbrangenciaDocumento.Nacional, "Cliente atendido", null, false, 40),
        new("alvara-sanitario", "Alvará sanitário", CategoriaDocumento.AmbientalSanitario, AbrangenciaDocumento.Municipal, "Vigilância sanitária", 365, false, 10),
        new("licenca-ambiental", "Licença ambiental", CategoriaDocumento.AmbientalSanitario, AbrangenciaDocumento.Estadual, "Órgão ambiental", null, false, 20),
        new("certidao-falencia", "Certidão de falência e recuperação judicial", CategoriaDocumento.Juridico, AbrangenciaDocumento.Estadual, "Tribunal de Justiça", 90, false, 10)
    };

    private sealed record NovoPacote(string Codigo, string Nome, string Descricao, int Ordem, string[] Tipos);

    private static readonly string[] Basicos = { "cnd-federal", "cnd-estadual", "cnd-municipal", "crf-fgts", "cndt", "cartao-cnpj", "contrato-social" };

    private static readonly NovoPacote[] Pacotes =
    {
        new("servicos-gerais", "Serviços em geral", "O básico que a maioria dos clientes pede.", 10,
            Basicos.Concat(new[] { "simples-nacional", "inscricao-municipal", "alvara-funcionamento" }).ToArray()),
        new("industria", "Presto serviço para indústria", "Segurança do trabalho e comprovantes mensais de quem trabalha dentro da fábrica.", 20,
            Basicos.Concat(new[] { "pgr", "pcmso", "ltcat", "seguro-vida", "fgts-pago-mes", "inss-pago-mes", "folha-pagamento" }).ToArray()),
        new("construcao", "Construção civil e obras", "Obra, responsabilidade técnica e segurança do trabalho.", 30,
            Basicos.Concat(new[] { "cno", "art", "registro-conselho", "pgr", "pcmso", "seguro-vida", "fgts-pago-mes", "inss-pago-mes" }).ToArray()),
        new("saude", "Clínicas e saúde", "Licenças sanitárias e registro profissional.", 40,
            Basicos.Concat(new[] { "alvara-funcionamento", "alvara-sanitario", "registro-conselho", "pgr", "pcmso" }).ToArray()),
        new("licitacao", "Vendo para órgão público (licitação)", "Habilitação: regularidade fiscal, situação financeira e capacidade técnica.", 50,
            Basicos.Concat(new[] { "inscricao-municipal", "inscricao-estadual", "certidao-falencia", "balanco-patrimonial", "atestado-capacidade" }).ToArray())
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db, default);
    }

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var codigos = await db.TiposDocumento.Where(t => t.Codigo != null)
            .ToDictionaryAsync(t => t.Codigo!, t => t.Id, cancellationToken);

        var faltando = Novos.Where(n => !codigos.ContainsKey(n.Codigo)).ToList();
        foreach (var n in faltando)
        {
            db.TiposDocumento.Add(new TipoDocumento
            {
                Codigo = n.Codigo,
                Nome = n.Nome,
                Categoria = n.Categoria,
                Abrangencia = n.Abrangencia,
                Orgao = n.Orgao,
                ValidadeDias = n.ValidadeDias,
                ExigeValidade = n.ExigeValidade,
                Ordem = n.Ordem
            });
        }
        if (faltando.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            codigos = await db.TiposDocumento.Where(t => t.Codigo != null)
                .ToDictionaryAsync(t => t.Codigo!, t => t.Id, cancellationToken);
        }

        var pacotesExistentes = await db.PacotesDocumento.Where(p => p.Codigo != null)
            .Select(p => p.Codigo!).ToListAsync(cancellationToken);
        var novosPacotes = Pacotes.Where(p => !pacotesExistentes.Contains(p.Codigo)).ToList();
        foreach (var p in novosPacotes)
        {
            db.PacotesDocumento.Add(new PacoteDocumento
            {
                Codigo = p.Codigo,
                Nome = p.Nome,
                Descricao = p.Descricao,
                Ordem = p.Ordem,
                TiposDocumentoIds = p.Tipos.Where(codigos.ContainsKey).Select(c => codigos[c]).Distinct().ToList()
            });
        }
        if (novosPacotes.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
