using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.DadosConta;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>D1 com o banco real: pedir, montar o ZIP, baixar e expirar a exportação dos dados.</summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ExportacaoDadosTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ExportacaoDadosTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class UsuarioFalso(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public string? IpAddress => "127.0.0.1";
    }

    /// <summary>Guarda os jobs em vez de rodar (o teste chama a geração na hora certa).</summary>
    private sealed class JobsFalsos : IBackgroundJobClient
    {
        public List<Job> Criados { get; } = [];
        public string Create(Job job, IState state)
        {
            Criados.Add(job);
            return Criados.Count.ToString();
        }
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class FilaFalsa : IBackgroundEmailQueue
    {
        public void Enfileirar(ItemFilaEmail item) { }
        public async IAsyncEnumerable<ItemFilaEmail> LerTodosAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static ExportacaoDadosService Servico(IServiceProvider sp, Guid usuarioId, JobsFalsos jobs, string pasta)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        return new ExportacaoDadosService(
            db,
            sp.GetRequiredService<ICurrentTenant>(),
            new UsuarioFalso(usuarioId),
            jobs,
            sp.GetRequiredService<IAuditLogWriter>(),
            new EmailQueueService(db, new FilaFalsa()),
            new MontadorExportacaoDados(db, sp.GetRequiredService<ContratoDocumentoFileStore>(), sp.GetRequiredService<IWebHostEnvironment>(), NullLogger<MontadorExportacaoDados>.Instance),
            Options.Create(new ExportacaoDadosOptions { BasePath = pasta }),
            Options.Create(new EmailOptions { AppBaseUrl = "https://app.teste" }),
            NullLogger<ExportacaoDadosService>.Instance);
    }

    [Fact]
    public async Task Administrador_pede_o_job_monta_o_zip_baixa_e_depois_expira()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var pasta = Path.Combine(Path.GetTempPath(), "teste-exportacao-" + s);

        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Exportação " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = conta.Id;
        var grupoAdmin = new Grupo { Nome = "Admin " + s, EhAdministrador = true };
        var grupoComum = new Grupo { Nome = "Comum " + s };
        db.Grupos.AddRange(grupoAdmin, grupoComum);
        await db.SaveChangesAsync();
        var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id };
        var comum = new ApplicationUser { TenantId = conta.Id, UserName = $"com{s}@t.com", Email = $"com{s}@t.com", GrupoId = grupoComum.Id };
        db.Users.AddRange(admin, comum);
        var empresa = new Empresa { Cnpj = s[..14], RazaoSocial = "Empresa " + s, TipoAmbiente = TipoAmbiente.Producao };
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();
        var cliente = new Cliente { EmpresaId = empresa.Id, Nome = "Padaria; Pão Quente", Apelido = "PADARIA", CpfCnpj = s[..11] };
        var servico = new Servico { EmpresaId = empresa.Id, Descricao = "Suporte" };
        db.Clientes.Add(cliente);
        db.Servicos.Add(servico);
        await db.SaveChangesAsync();
        var contrato = new Contrato { EmpresaId = empresa.Id, ClienteId = cliente.Id, Descricao = "Mensal", ValorAtual = 150, DataInicioContrato = new DateOnly(2026, 1, 1) };
        db.Contratos.Add(contrato);
        await db.SaveChangesAsync();
        db.ContratoServicos.Add(new ContratoServico { ContratoId = contrato.Id, ServicoId = servico.Id, Quantidade = 1, ValorUnitario = 150 });
        // Documento sem arquivo no disco: vira pendência, não derruba a exportação.
        db.ContratoDocumentos.Add(new ContratoDocumento { EmpresaId = empresa.Id, ContratoId = contrato.Id, NomeOriginal = "contrato.pdf", Extensao = ".pdf", TamanhoBytes = 10 });
        db.NotasFiscais.Add(new Nfse
        {
            EmpresaId = empresa.Id, ClienteId = cliente.Id, ContratoId = contrato.Id, ServicoId = servico.Id,
            NumeroDps = 7, SerieDps = "1", NumeroNfse = "42", ChaveAcesso = "CHAVE" + s,
            DataCompetencia = new DateOnly(2026, 10, 1), DataEmissao = new DateTimeOffset(2026, 10, 5, 13, 0, 0, TimeSpan.Zero),
            ValorServico = 150m, DescricaoServico = "Suporte mensal", CodigoTributacaoNacional = "010101", CodigoNbs = "115022000",
            TribIssqn = "1", TpRetIssqn = "1", CstPisCofins = "00", TpRetPisCofins = "1",
            Status = NfseStatus.Autorizada, TipoAmbiente = TipoAmbiente.Producao,
            // XML mínimo: o DANFSe é gerado localmente a partir dele.
            XmlNfse = "<NFSe xmlns=\"http://www.sped.fazenda.gov.br/nfse\"><infNFSe Id=\"NFS1\"/></NFSe>"
        });
        db.NotasFiscais.Add(new Nfse
        {
            EmpresaId = empresa.Id, ClienteId = cliente.Id, NumeroDps = 8, SerieDps = "1", DataCompetencia = new DateOnly(2026, 11, 1),
            ValorServico = 10m, Status = NfseStatus.Rejeitada, TipoAmbiente = TipoAmbiente.Producao, CodigoErro = "E0001"
        });
        await db.SaveChangesAsync();
        db.TenantIdOverrideDeSistema = null;
        db.ChangeTracker.Clear();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(conta.Id);

        var jobs = new JobsFalsos();
        try
        {
            // Quem não é administrador não pede nem vê a lista.
            var servicoComum = Servico(sp, comum.Id, jobs, pasta);
            var visaoComum = await servicoComum.ListarAsync(CancellationToken.None);
            Assert.False(visaoComum.PodeExportar);
            Assert.Equal(RegrasExportacaoDados.MotivoSoAdministrador, visaoComum.Motivo);
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoComum.SolicitarAsync(CancellationToken.None));

            // Administrador pede: fica na fila e o job é agendado com a conta e o pedido.
            var servicoAdmin = Servico(sp, admin.Id, jobs, pasta);
            var pedido = await servicoAdmin.SolicitarAsync(CancellationToken.None);
            Assert.Equal(StatusExportacaoDados.Pendente, pedido.Status);
            var job = Assert.Single(jobs.Criados);
            Assert.Equal(nameof(NfseSaaS.Infrastructure.Jobs.IExportacaoDadosJob.GerarAsync), job.Method.Name);
            Assert.Equal(new object[] { conta.Id, pedido.Id }, job.Args);

            // Um pedido por vez.
            var emAndamento = await Assert.ThrowsAsync<RegraNegocioException>(() => servicoAdmin.SolicitarAsync(CancellationToken.None));
            Assert.Equal(RegrasExportacaoDados.MotivoEmAndamento, emAndamento.Message);

            // O job monta o arquivo.
            await servicoAdmin.GerarAsync(pedido.Id, CancellationToken.None);
            var lista = await servicoAdmin.ListarAsync(CancellationToken.None);
            var pronta = Assert.Single(lista.Exportacoes);
            Assert.Equal(StatusExportacaoDados.Pronta, pronta.Status);
            Assert.True(pronta.PodeBaixar);
            Assert.Equal(2, pronta.QuantidadeNotas);
            // Pronta valendo: não monta outra (baixa essa).
            Assert.Equal(RegrasExportacaoDados.MotivoJaTemPronta(pronta.ExpiraEm!.Value), lista.Motivo);
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoAdmin.SolicitarAsync(CancellationToken.None));

            // Aviso por e-mail para quem pediu.
            Assert.True(await db.EmailLogs.AnyAsync(e => e.Tipo == TipoEmailCatalogo.ExportacaoDados && e.Destinatario == admin.Email));

            // Download: só administrador.
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoComum.BaixarAsync(pedido.Id, CancellationToken.None));
            var arquivo = await servicoAdmin.BaixarAsync(pedido.Id, CancellationToken.None);
            Assert.StartsWith("micoempresa-dados-" + s[..14], arquivo.NomeArquivo);

            using (var zip = ZipFile.OpenRead(arquivo.Caminho))
            {
                string Ler(string nome)
                {
                    using var leitor = new StreamReader(zip.GetEntry(nome)!.Open(), Encoding.UTF8);
                    return leitor.ReadToEnd();
                }

                var nomes = zip.Entries.Select(e => e.FullName).ToList();
                foreach (var tabela in new[] { "conta", "usuarios", "empresas", "clientes", "servicos", "contratos", "contratos_servicos",
                                               "contratos_historico_valores", "contratos_documentos", "notas", "notas_eventos" })
                {
                    Assert.Contains($"dados/{tabela}.csv", nomes);
                    Assert.Contains($"dados/{tabela}.json", nomes);
                }

                // XML da nota autorizada, por empresa/ambiente/mês; a rejeitada só na planilha.
                var xml = Assert.Single(nomes, n => n.EndsWith(".xml"));
                Assert.Equal($"notas/{s[..14]}/producao/2026-10/PADARIA_7_05-10-2026.xml", xml);
                Assert.Contains("infNFSe", Ler(xml));
                var pdf = Path.ChangeExtension(xml, ".pdf");
                Assert.Contains(pdf, nomes);
                using (var leitorPdf = new StreamReader(zip.GetEntry(pdf)!.Open()))
                    Assert.StartsWith("%PDF", leitorPdf.ReadToEnd());

                Assert.Contains("\"Padaria; Pão Quente\"", Ler("dados/clientes.csv"));
                var notas = Ler("dados/notas.csv");
                Assert.Contains("E0001", notas);
                Assert.Contains(xml, notas);
                Assert.Contains($"adm{s}@t.com", Ler("dados/usuarios.csv"));

                var leiame = Ler("LEIAME.txt");
                Assert.Contains("Notas: 2", leiame);
                Assert.Contains("PENDÊNCIAS", leiame);
                Assert.Contains("contrato.pdf", leiame);
                Assert.DoesNotContain("o PDF não pôde ser gerado", leiame);

                // Nada de segredo no arquivo.
                Assert.DoesNotContain(nomes, n => n.Contains("certificad", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".pfx"));
                Assert.DoesNotContain("PasswordHash", Ler("dados/usuarios.json"));
            }

            Assert.True(await db.AuditLogs.AnyAsync(l => l.Operacao == "BaixarExportacaoDados"));

            // XML do mês (tela de notas): só a autorizada de out/2026, sem limite de vezes.
            var xmlMes = sp.GetRequiredService<NfseSaaS.Application.UseCases.Nfse.IBaixarXmlMesUseCase>();
            for (var vez = 0; vez < 2; vez++)
            {
                var mes = await xmlMes.ExecutarAsync(empresa.Id, new DateOnly(2026, 10, 20), null, CancellationToken.None);
                Assert.Equal(1, mes.Quantidade);
                Assert.Equal(NfseSaaS.Application.UseCases.Nfse.RegrasXmlMes.NomeArquivo(s[..14], new DateOnly(2026, 10, 1), TipoAmbiente.Producao), mes.NomeArquivoZip);
                using var zipMes = new ZipArchive(new MemoryStream(mes.ZipBytes));
                Assert.Equal("PADARIA_7_05-10-2026.xml", Assert.Single(zipMes.Entries).FullName);
            }
            await Assert.ThrowsAsync<RegraNegocioException>(() => xmlMes.ExecutarAsync(empresa.Id, new DateOnly(2026, 11, 1), null, CancellationToken.None)); // só rejeitada
            await Assert.ThrowsAsync<RegraNegocioException>(() => xmlMes.ExecutarAsync(empresa.Id, new DateOnly(2026, 10, 1), TipoAmbiente.Homologacao, CancellationToken.None));

            // Operador libera: a pronta expira, o arquivo some e a conta pode pedir outra.
            var caminhoAntes = arquivo.Caminho;
            await servicoAdmin.LiberarNovaPelaPlataformaAsync(conta.Id, Guid.NewGuid(), CancellationToken.None);
            Assert.False(File.Exists(caminhoAntes));
            db.ChangeTracker.Clear();
            Assert.Null((await servicoAdmin.ListarAsync(CancellationToken.None)).Motivo);
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoAdmin.LiberarNovaPelaPlataformaAsync(conta.Id, Guid.NewGuid(), CancellationToken.None));

            // Pede de novo (2º pedido do dia) e monta, para seguir com a limpeza abaixo.
            var segundo = await servicoAdmin.SolicitarAsync(CancellationToken.None);
            await servicoAdmin.GerarAsync(segundo.Id, CancellationToken.None);
            arquivo = await servicoAdmin.BaixarAsync(segundo.Id, CancellationToken.None);

            // Limpeza depois dos 7 dias: expira e apaga o arquivo.
            var limpeza = await servicoAdmin.LimparAsync(DateTimeOffset.UtcNow.AddDays(8), CancellationToken.None);
            Assert.True(limpeza.Expiradas >= 1);
            Assert.False(File.Exists(arquivo.Caminho));
            db.ChangeTracker.Clear();
            var depois = (await servicoAdmin.ListarAsync(CancellationToken.None)).Exportacoes;
            Assert.Equal(2, depois.Count);
            Assert.All(depois, e => Assert.Equal(StatusExportacaoDados.Expirada, e.Status));
            Assert.All(depois, e => Assert.False(e.PodeBaixar));
            await Assert.ThrowsAsync<RegraNegocioException>(() => servicoAdmin.BaixarAsync(segundo.Id, CancellationToken.None));
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ExportacoesDados.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoDocumentos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ContratoServicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Contratos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Servicos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }
}
