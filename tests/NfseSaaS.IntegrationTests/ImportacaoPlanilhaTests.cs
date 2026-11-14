using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.UseCases.Importacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// IM1 de ponta a ponta, com o banco real: a conferência não grava nada e
/// diz o que acontece com cada linha; a gravação cadastra clientes (inclusive
/// com nome e endereço da "Receita"), atualiza só o que veio preenchido, liga
/// contratos a clientes da própria planilha, monta contrato com dois serviços
/// e não duplica nada quando o mesmo arquivo volta.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ImportacaoPlanilhaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ImportacaoPlanilhaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private static readonly PermissoesImportacao Tudo = new(true, true, true);

    [Fact]
    public async Task Confere_grava_e_nao_duplica()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Importação " + s, Perfil = PerfilConta.Contador };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            var empresa = new Empresa
            {
                Cnpj = "04747304000178", RazaoSocial = "Escritório " + s, NomeFantasia = "Escritório", InscricaoMunicipal = "1",
                CodigoMunicipio = "2927408", Uf = "BA", Cep = "40010000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro",
                Telefone = "71999999999", Email = "empresa@teste.com", TipoAmbiente = TipoAmbiente.Homologacao,
                OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08",
                PercentualTotalTributosSimplesNacional = "6.00"
            };
            db.Empresas.Add(empresa);
            var manutencao = new Servico { EmpresaId = empresa.Id, Descricao = "Manutenção mensal", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            var suporte = new Servico { EmpresaId = empresa.Id, Descricao = "Suporte técnico", CodigoTributacaoNacional = "010501", CodigoNbs = "115013000" };
            db.Servicos.AddRange(manutencao, suporte);
            var antigo = new Cliente
            {
                EmpresaId = empresa.Id, CpfCnpj = "11222333000181", Nome = "CLIENTE ANTIGO LTDA", Apelido = "Antigo",
                Email = "velho@antigo.com", Cep = "40010000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro",
                CodigoMunicipio = "2927408", Uf = "BA"
            };
            db.Clientes.Add(antigo);
            db.Contratos.Add(new Contrato
            {
                EmpresaId = empresa.Id, ClienteId = antigo.Id, Numero = "JA-1", ValorAtual = 10, DataInicioContrato = new DateOnly(2025, 1, 1),
                TipoCobranca = TipoCobrancaContrato.Mensal, IndiceReajuste = IndiceReajusteContrato.Ipca
            });
            await db.SaveChangesAsync();

            var arquivo = new ArquivoImportacao("migracao.xlsx", Planilha(
                clientes:
                [
                    ["CPF/CNPJ", "Razão social", "Fantasia", "E-mail", "CEP", "Endereço", "Número", "Bairro", "Cidade", "UF", "Código no sistema antigo"],
                    ["12.345.678/0001-95", "PADARIA BOM PAO LTDA", "Bom Pão", "compras@bompao.com; financeiro@bompao.com", "40.010-000", "Av. Sete", "", "Centro", "Salvador", "BA", "C-77"],
                    ["11444777000161", "", "", "", "", "", "", "", "", "", "C-78"],          // só o CNPJ: nome e endereço da Receita
                    ["11.222.333/0001-81", "", "", "novo@antigo.com", "", "", "", "", "", "", "C-79"], // já existe: atualiza o e-mail
                    ["123.456.789-00", "CPF ERRADO", "", "", "", "", "", "", "", "", ""],
                    ["12345678000195", "REPETIDO", "", "", "", "", "", "", "", "", ""],
                    [1234567890m, "Fulana de Tal", "", "", "", "", "", "", "", "", ""]   // CPF que perdeu o zero
                ],
                contratos:
                [
                    ["Nº do contrato", "CPF/CNPJ do cliente", "Serviço", "Valor unitário", "Início", "Índice de reajuste"],
                    ["IMP-1", "12345678000195", "manutencao mensal", "1.500,00", new DateTime(2025, 3, 1), "IGP-M"],
                    ["IMP-1", "", "Suporte técnico", 250m, "", ""],
                    ["", "11.222.333/0001-81", "Suporte técnico", "300", "01/03/2023", ""],
                    ["IMP-2", "01234567890", "Serviço que não existe", "100", "01/01/2026", ""],
                    ["IMP-3", "123.456.789-00", "Suporte técnico", "100", "01/01/2026", ""],
                    ["JA-1", "11222333000181", "Suporte técnico", "100", "01/01/2026", ""]
                ]));

            var importacao = sp.GetRequiredService<IImportacaoPlanilhaUseCase>();

            // ---------- Conferir: nada gravado ----------
            var conferencia = await importacao.ConferirAsync(empresa.Id, arquivo, new OpcoesImportacao(AtualizarExistentes: true), Tudo, CancellationToken.None);
            Assert.False(conferencia.Gravado);
            Assert.Equal(1, await db.Clientes.CountAsync());

            var clientes = conferencia.Abas.Single(a => a.Tipo == "clientes");
            Assert.Contains("Código no sistema antigo", clientes.ColunasIgnoradas);
            Assert.Contains(clientes.ColunasReconhecidas, c => c.StartsWith("Cidade → "));
            var porLinha = clientes.Linhas.ToDictionary(l => l.Linha);
            Assert.Equal(SituacaoLinhaImportacao.Nova, porLinha[2].Situacao);
            Assert.Contains(porLinha[2].Avisos, a => a.Contains("S/N"));
            Assert.Contains(porLinha[2].Avisos, a => a.Contains("cópias"));
            Assert.Equal(SituacaoLinhaImportacao.Nova, porLinha[3].Situacao);
            Assert.Contains(porLinha[3].Avisos, a => a.Contains("Receita"));
            Assert.Equal(SituacaoLinhaImportacao.Atualiza, porLinha[4].Situacao);
            Assert.Contains(porLinha[4].Mudancas, m => m.StartsWith("E-mail: velho@antigo.com → novo@antigo.com"));
            Assert.Equal(SituacaoLinhaImportacao.Erro, porLinha[5].Situacao);
            Assert.Equal(SituacaoLinhaImportacao.Erro, porLinha[6].Situacao);
            Assert.Contains(porLinha[6].Erros, e => e.Contains("linha 2"));
            Assert.Equal(SituacaoLinhaImportacao.Nova, porLinha[7].Situacao);
            Assert.Equal("012.345.678-90", porLinha[7].Chave);
            Assert.Equal(new TotaisImportacao(3, 1, 0, 0, 2), conferencia.Clientes);

            var contratos = conferencia.Abas.Single(a => a.Tipo == "contratos").Linhas.ToDictionary(l => l.Linha);
            Assert.Equal(SituacaoLinhaImportacao.Nova, contratos[2].Situacao);
            Assert.Equal(SituacaoLinhaImportacao.Nova, contratos[3].Situacao);
            Assert.Equal(SituacaoLinhaImportacao.Nova, contratos[4].Situacao);
            Assert.Contains(contratos[4].Avisos, a => a.Contains("IPCA"));
            Assert.Contains(contratos[4].Avisos, a => a.Contains("último aniversário"));
            Assert.Equal(SituacaoLinhaImportacao.Erro, contratos[5].Situacao);
            Assert.Contains(contratos[5].Erros, e => e.Contains("não encontrado"));
            Assert.Equal(SituacaoLinhaImportacao.Erro, contratos[6].Situacao);
            Assert.Equal(SituacaoLinhaImportacao.JaExiste, contratos[7].Situacao);
            // Contrato com dois serviços conta uma vez.
            Assert.Equal(new TotaisImportacao(2, 0, 1, 0, 2), conferencia.Contratos);

            // ---------- Gravar ----------
            var gravacao = await importacao.GravarAsync(empresa.Id, arquivo, new OpcoesImportacao(AtualizarExistentes: true), Tudo, CancellationToken.None);
            Assert.True(gravacao.Gravado);
            Assert.Equal(conferencia.Clientes, gravacao.Clientes);
            Assert.Equal(conferencia.Contratos, gravacao.Contratos);
            db.ChangeTracker.Clear();

            var padaria = await db.Clientes.SingleAsync(c => c.CpfCnpj == "12345678000195");
            Assert.Equal("Bom Pão", padaria.Apelido);
            Assert.Equal("compras@bompao.com", padaria.Email);
            Assert.Equal("financeiro@bompao.com", padaria.EmailsCopia);
            Assert.True(padaria.EnviarNfsePorEmail);
            Assert.Equal("S/N", padaria.Numero);
            Assert.Equal("2927408", padaria.CodigoMunicipio);
            Assert.Equal(empresa.Id, padaria.EmpresaId); // conta de contador: só esta empresa

            var daReceita = await db.Clientes.SingleAsync(c => c.CpfCnpj == "11444777000161");
            Assert.Equal("EMPRESA TESTE 11444777000161 LTDA", daReceita.Nome);
            Assert.Equal("Fantasia Teste", daReceita.Apelido);
            Assert.Equal("Rua do Teste", daReceita.Logradouro);

            var atualizado = await db.Clientes.SingleAsync(c => c.Id == antigo.Id);
            Assert.Equal("novo@antigo.com", atualizado.Email);
            Assert.Equal("CLIENTE ANTIGO LTDA", atualizado.Nome); // célula vazia não apaga
            Assert.Equal("Rua B", atualizado.Logradouro);

            Assert.Equal("Fulana de Tal", (await db.Clientes.SingleAsync(c => c.CpfCnpj == "01234567890")).Nome);
            Assert.Equal(4, await db.Clientes.CountAsync());

            var imp1 = await db.Contratos.SingleAsync(c => c.Numero == "IMP-1");
            Assert.Equal(padaria.Id, imp1.ClienteId);
            Assert.Equal(1_750m, imp1.ValorAtual);
            Assert.Equal(IndiceReajusteContrato.Igpm, imp1.IndiceReajuste);
            Assert.Equal(2, await db.ContratoServicos.CountAsync(cs => cs.ContratoId == imp1.Id));
            Assert.True(await db.MovimentacoesValorContrato.AnyAsync(m => m.ContratoId == imp1.Id && m.Tipo == TipoMovimentacaoValor.ValorInicial));

            var semNumero = await db.Contratos.SingleAsync(c => c.ClienteId == antigo.Id && c.Numero != "JA-1");
            Assert.Matches(@"^\d{4}/\d{4}$", semNumero.Numero!);
            Assert.Equal(IndiceReajusteContrato.Ipca, semNumero.IndiceReajuste);
            Assert.Equal(RegrasImportacao.UltimoAniversario(new DateOnly(2023, 3, 1), 12, DataBrasilia.Hoje()), semNumero.DataUltimoReajuste);

            Assert.True(await db.AuditLogs.AnyAsync(a => a.Operacao == "ImportacaoPlanilha" && a.EntidadeId == empresa.Id));
            Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Operacao == "CadastrarCliente"));

            // ---------- O mesmo arquivo de novo: nada duplica ----------
            var deNovo = await importacao.ConferirAsync(empresa.Id, arquivo, new OpcoesImportacao(), Tudo, CancellationToken.None);
            Assert.Equal(new TotaisImportacao(0, 0, 4, 0, 2), deNovo.Clientes);
            var contratosDeNovo = deNovo.Abas.Single(a => a.Tipo == "contratos").Linhas.ToDictionary(l => l.Linha);
            Assert.Equal(SituacaoLinhaImportacao.JaExiste, contratosDeNovo[2].Situacao);
            Assert.Equal(SituacaoLinhaImportacao.JaExiste, contratosDeNovo[4].Situacao);
            Assert.Contains(contratosDeNovo[4].Avisos, a => a.Contains("mesmo cliente e os mesmos serviços"));

            // ---------- Sem permissão de contratos ----------
            var semContratos = await importacao.ConferirAsync(empresa.Id, new ArquivoImportacao("c.xlsx", Planilha(
                    clientes: null,
                    contratos: [["CPF/CNPJ do cliente", "Serviço", "Valor", "Início"], ["11222333000181", "Manutenção mensal", "50", "01/10/2026"]])),
                new OpcoesImportacao(), new PermissoesImportacao(true, false, false), CancellationToken.None);
            Assert.Contains(semContratos.Abas.Single().Linhas.Single().Erros, e => e.Contains("permissão"));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>Planilha .xlsx com as abas Clientes e Contratos; primeira linha = cabeçalho. Valor decimal/DateTime vira célula numérica/data.</summary>
    private static byte[] Planilha(object[][]? clientes, object[][]? contratos)
    {
        using var pasta = new XLWorkbook();
        void Aba(string nome, object[][]? linhas)
        {
            if (linhas is null)
                return;
            var aba = pasta.AddWorksheet(nome);
            for (var l = 0; l < linhas.Length; l++)
            for (var c = 0; c < linhas[l].Length; c++)
            {
                var celula = aba.Cell(l + 1, c + 1);
                switch (linhas[l][c])
                {
                    case decimal d: celula.Value = d; break;
                    case DateTime data: celula.Value = data; break;
                    case string texto when texto.Length > 0: celula.Value = texto; break;
                }
            }
        }
        Aba("Clientes", clientes);
        Aba("Contratos", contratos);
        using var memoria = new MemoryStream();
        pasta.SaveAs(memoria);
        return memoria.ToArray();
    }
}
