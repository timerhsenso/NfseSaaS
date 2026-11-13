using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.PainelFinanceiro;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// DB1: o painel conta só nota de produção autorizada (pela competência),
/// separa contrato de avulsa, acha os contratos sem nota no mês (com a
/// rejeitada na frente), lista os reajustes chegando e respeita o escopo
/// (todas as empresas x uma) e a permissão de Contratos.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class PainelFinanceiroTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 13);
    private readonly AplicacaoTesteFactory _factory;

    public PainelFinanceiroTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Painel_soma_o_que_vale_e_aponta_o_que_falta()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Painel " + s, Perfil = PerfilConta.Empresa };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        try
        {
            Empresa NovaEmpresa(string nome, string cnpj, TipoAmbiente ambiente) => new()
            {
                Cnpj = cnpj, RazaoSocial = nome + " " + s, NomeFantasia = nome, InscricaoMunicipal = "12345",
                CodigoMunicipio = "2919207", Uf = "BA", Cep = "42700000", Logradouro = "Rua A", Numero = "1", Bairro = "Centro",
                Telefone = "71999999999", Email = "empresa@teste.com", TipoAmbiente = ambiente,
                OpSimpNac = "3", RegApTribSN = "1", RegEspTrib = "0", TribIssqn = "1", CstPisCofins = "08",
                PercentualTotalTributosSimplesNacional = "6.00"
            };
            var matriz = NovaEmpresa("Matriz", "04747304000178", TipoAmbiente.Producao);
            var filial = NovaEmpresa("Filial", "11222333000181", TipoAmbiente.Homologacao);
            db.Empresas.AddRange(matriz, filial);
            await db.SaveChangesAsync();

            Cliente NovoCliente(string apelido, string doc) => new()
            {
                EmpresaId = null, CpfCnpj = doc, Nome = apelido, Apelido = apelido + " " + s[..4],
                Cep = "42700000", Logradouro = "Rua B", Numero = "2", Bairro = "Centro", CodigoMunicipio = "2919207", Uf = "BA"
            };
            var holtz = NovoCliente("HOLTZ", "52998224725");
            var copenor = NovoCliente("COPENOR", "11144477735");
            var avulso = NovoCliente("AVULSO", "39053344705");
            db.Clientes.AddRange(holtz, copenor, avulso);
            await db.SaveChangesAsync();

            Contrato NovoContrato(Empresa empresa, Cliente cliente, string numero, decimal valor, DateOnly inicio, IndiceReajusteContrato? indice) => new()
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, Numero = numero, ValorAtual = valor, DataInicioContrato = inicio,
                TipoCobranca = TipoCobrancaContrato.Mensal, Status = StatusContrato.Ativo, PeriodicidadeReajusteMeses = 12, IndiceReajuste = indice
            };
            // Aniversário 20/11/2026, sem índice: entra nos reajustes sem estimativa.
            var cHoltz = NovoContrato(matriz, holtz, "2025/0001", 1_000m, new DateOnly(2025, 11, 20), null);
            // Sem nota autorizada em novembro (só rejeitada): a emitir, na frente.
            var cCopenor = NovoContrato(matriz, copenor, "2025/0002", 500m, new DateOnly(2025, 3, 1), IndiceReajusteContrato.Outro);
            // Começa em dezembro: não é receita de hoje nem a emitir em novembro.
            var cFuturo = NovoContrato(matriz, copenor, "2026/0003", 300m, new DateOnly(2026, 12, 1), null);
            // Filial em homologação: receita recorrente sim, a emitir não.
            var cFilial = NovoContrato(filial, holtz, "2026/0004", 200m, new DateOnly(2026, 1, 15), null);
            db.Contratos.AddRange(cHoltz, cCopenor, cFuturo, cFilial);
            await db.SaveChangesAsync();

            // Reajuste da COPENOR aguardando aprovação (vigência 01/03/2026).
            db.MovimentacoesValorContrato.Add(new MovimentacaoValorContrato
            {
                ContratoId = cCopenor.Id, Tipo = TipoMovimentacaoValor.Reajuste, Status = StatusMovimentacaoValor.Pendente,
                DataVigencia = new DateOnly(2026, 3, 1), ValorAnterior = 500m, ValorNovo = 525m, PercentualCalculado = 5m
            });

            var numero = 0;
            Nfse NovaNota(Empresa empresa, Cliente cliente, Contrato? contrato, DateOnly competencia, decimal valor,
                NfseStatus status = NfseStatus.Autorizada, TipoAmbiente ambiente = TipoAmbiente.Producao) => new()
            {
                EmpresaId = empresa.Id, ClienteId = cliente.Id, ContratoId = contrato?.Id, NumeroDps = 900_000 + ++numero, SerieDps = "1",
                DataCompetencia = competencia, DataEmissao = DateTimeOffset.UtcNow, ValorServico = valor, DescricaoServico = "Teste",
                CodigoTributacaoNacional = "010501", CodigoNbs = "115013000", TribIssqn = "1", TpRetIssqn = "1",
                CstPisCofins = "08", TpRetPisCofins = "0", PercentualTotalTributosSimplesNacional = "6.00",
                Status = status, TipoAmbiente = ambiente
            };
            db.NotasFiscais.AddRange(
                NovaNota(matriz, holtz, cHoltz, new DateOnly(2026, 11, 1), 1_000m),
                NovaNota(matriz, avulso, null, new DateOnly(2026, 10, 5), 300m),
                NovaNota(matriz, holtz, cHoltz, new DateOnly(2026, 10, 1), 1_000m),
                NovaNota(matriz, holtz, cHoltz, new DateOnly(2025, 12, 1), 900m),
                // Não contam: homologação, cancelada, rejeitada e fora da janela.
                NovaNota(matriz, copenor, cCopenor, new DateOnly(2026, 11, 1), 5_000m, ambiente: TipoAmbiente.Homologacao),
                NovaNota(matriz, copenor, cCopenor, new DateOnly(2026, 11, 1), 777m, NfseStatus.Cancelada),
                NovaNota(matriz, copenor, cCopenor, new DateOnly(2026, 11, 1), 500m, NfseStatus.Rejeitada),
                NovaNota(matriz, holtz, cHoltz, new DateOnly(2025, 10, 1), 50_000m));
            await db.SaveChangesAsync();

            var painel = sp.GetRequiredService<IObterPainelFinanceiroUseCase>();

            // Perfil Empresa: padrão = todas as empresas.
            var todas = await painel.ExecutarAsync(new PainelFinanceiroRequest(matriz.Id, null, true, Hoje), CancellationToken.None);
            Assert.Equal(EscopoPainelFinanceiro.Todas, todas.Escopo);
            Assert.Equal(2, todas.QuantidadeEmpresas);
            Assert.Equal(1, todas.EmpresasEmHomologacao);
            Assert.Null(todas.Limite); // soma de empresas não fala de limite de nenhuma
            Assert.True(todas.TemDados);

            Assert.Equal(1_000m, todas.MesAtual.Total);
            Assert.Equal(1, todas.MesAtual.Notas);
            Assert.Equal(1_000m, todas.MesAnterior.Contratos);
            Assert.Equal(300m, todas.MesAnterior.Avulsas);
            Assert.Equal(2_300m, todas.AcumuladoAno);
            Assert.Equal(2_200m, todas.Ultimos12Meses); // dez/25 a out/26; novembro é o mês em andamento
            Assert.Equal(12, todas.Meses.Count);
            Assert.Equal(3_200m, todas.Meses.Sum(m => m.Total));
            Assert.Equal(holtz.Id, todas.MaioresClientes[0].ClienteId);
            Assert.Equal(3_200m, todas.TotalMaioresClientesJanela);

            var c = Assert.IsType<ContratosPainelResponse>(todas.Contratos);
            Assert.Equal(1_700m, c.ReceitaRecorrente); // HOLTZ + COPENOR + filial; o de dezembro ainda não
            Assert.Equal(3, c.ContratosMensais);
            var aEmitir = Assert.Single(c.AEmitir);
            Assert.Equal(cCopenor.Id, aEmitir.ContratoId);
            Assert.True(aEmitir.Rejeitada);
            Assert.Equal(500m, c.AEmitirValor);

            Assert.Equal(new DateOnly(2026, 12, 31), c.ReajustesAte);
            var aguardando = Assert.Single(c.Reajustes, r => r.ContratoId == cCopenor.Id);
            Assert.Equal("aguardando", aguardando.Situacao);
            Assert.Equal(525m, aguardando.ValorNovo);
            Assert.True(aguardando.Atrasado);
            var semIndice = Assert.Single(c.Reajustes, r => r.ContratoId == cHoltz.Id);
            Assert.Equal("sem-indice", semIndice.Situacao);
            Assert.Equal(new DateOnly(2026, 11, 20), semIndice.Data);
            Assert.False(semIndice.Atrasado);
            Assert.DoesNotContain(c.Reajustes, r => r.ContratoId == cFilial.Id); // aniversário só em jan/27
            Assert.Equal(25m, c.ReajustesImpactoEstimado);
            Assert.Equal(1, c.ReajustesSemEstimativa);

            // Só a filial: nenhuma nota de produção, nada a emitir (homologação), limite do Simples aparece.
            var soFilial = await painel.ExecutarAsync(new PainelFinanceiroRequest(filial.Id, EscopoPainelFinanceiro.Empresa, true, Hoje), CancellationToken.None);
            Assert.Equal(EscopoPainelFinanceiro.Empresa, soFilial.Escopo);
            Assert.Equal(filial.Id, soFilial.EmpresaId);
            Assert.Equal(0m, soFilial.AcumuladoAno);
            Assert.Equal(200m, soFilial.Contratos!.ReceitaRecorrente);
            Assert.Empty(soFilial.Contratos.AEmitir);
            Assert.Equal("simples", soFilial.Limite!.Tipo);

            // Sem permissão de Contratos: nada de contratos. Empresa de outra conta: vira "todas".
            var semContratos = await painel.ExecutarAsync(new PainelFinanceiroRequest(Guid.NewGuid(), EscopoPainelFinanceiro.Empresa, false, Hoje), CancellationToken.None);
            Assert.Null(semContratos.Contratos);
            Assert.Equal(EscopoPainelFinanceiro.Todas, semContratos.Escopo);
            Assert.Equal(2_300m, semContratos.AcumuladoAno);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.MovimentacoesValorContrato.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
