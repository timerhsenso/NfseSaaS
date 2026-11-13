using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.EmailNota;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// E-mail da nota com o banco real: modelos reutilizáveis (padrão da conta,
/// da empresa e do cliente), troca de modelo e texto no modal só para um
/// envio, responder-para e cópia oculta fixa, o envio de várias notas
/// (resumo, sem e-mail, não autorizada, já enviada) e excluir modelo em uso.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class EmailNotaTests
{
    private readonly AplicacaoTesteFactory _factory;

    public EmailNotaTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class FilaFalsa : IBackgroundEmailQueue
    {
        public List<ItemFilaEmail> Itens { get; } = new();
        public void Enfileirar(ItemFilaEmail item) => Itens.Add(item);
        public async IAsyncEnumerable<ItemFilaEmail> LerTodosAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class UsuarioFalso(Guid id) : ICurrentUser
    {
        public Guid? UserId { get; } = id;
        public string? IpAddress => "127.0.0.1";
    }

    [Fact]
    public async Task Modelos_reutilizaveis_por_empresa_e_cliente_modal_e_envio_em_lote()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var s = Guid.NewGuid().ToString("N");
        var conta = new Tenant { Documento = s[..14], RazaoSocial = "Email nota " + s };
        db.Tenants.Add(conta);
        await db.SaveChangesAsync();

        try
        {
            var tenantAtual = sp.GetRequiredService<ICurrentTenant>();
            tenantAtual.DefinirTenantIdDeSistema(conta.Id);

            var grupoAdmin = new Grupo { Nome = "Administrador", EhAdministrador = true };
            var grupoComum = new Grupo { Nome = "Operador" };
            db.Grupos.AddRange(grupoAdmin, grupoComum);
            var empresaA = new Empresa { Cnpj = s[..14], RazaoSocial = "Alfa Serviços LTDA", NomeFantasia = "Alfa", Email = "contato@alfa.com", TipoAmbiente = TipoAmbiente.Producao };
            var empresaB = new Empresa { Cnpj = s[1..15], RazaoSocial = "Beta LTDA", Email = "beta@beta.com", TipoAmbiente = TipoAmbiente.Producao };
            db.Empresas.AddRange(empresaA, empresaB);
            await db.SaveChangesAsync();
            var admin = new ApplicationUser { TenantId = conta.Id, UserName = $"adm{s}@t.com", Email = $"adm{s}@t.com", GrupoId = grupoAdmin.Id };
            var comum = new ApplicationUser { TenantId = conta.Id, UserName = $"op{s}@t.com", Email = $"op{s}@t.com", GrupoId = grupoComum.Id };
            db.Users.AddRange(admin, comum);

            Cliente NovoCliente(Empresa e, string apelido, string? email) => new()
            {
                EmpresaId = e.Id, Nome = "Cliente " + apelido, Apelido = apelido + s[..4], CpfCnpj = Guid.NewGuid().ToString("N")[..11], Email = email
            };
            var comEmail = NovoCliente(empresaA, "COM", "cliente@teste.com");
            var semEmail = NovoCliente(empresaA, "SEM", null);
            var deB = NovoCliente(empresaB, "B", "b@teste.com");
            db.Clientes.AddRange(comEmail, semEmail, deB);
            await db.SaveChangesAsync();

            var numero = 0;
            Nfse NovaNota(Empresa e, Cliente c, NfseStatus status) => new()
            {
                EmpresaId = e.Id, ClienteId = c.Id, NumeroDps = ++numero, SerieDps = "1", NumeroNfse = (100 + numero).ToString(),
                ValorServico = 344.21m, DataCompetencia = new DateOnly(2026, 11, 1), DataEmissao = DateTimeOffset.UtcNow,
                DescricaoServico = "Manutenção", Status = status, TipoAmbiente = TipoAmbiente.Producao, ChaveAcesso = "CH" + s + numero
            };
            var nota1 = NovaNota(empresaA, comEmail, NfseStatus.Autorizada);
            var nota2 = NovaNota(empresaA, semEmail, NfseStatus.Autorizada);
            var nota3 = NovaNota(empresaA, comEmail, NfseStatus.Rejeitada);
            var nota4 = NovaNota(empresaB, deB, NfseStatus.Autorizada);
            db.NotasFiscais.AddRange(nota1, nota2, nota3, nota4);
            await db.SaveChangesAsync();

            var fila = new FilaFalsa();
            var auditoria = sp.GetRequiredService<IAuditLogWriter>();
            var plataforma = sp.GetRequiredService<IOptions<PlataformaOptions>>();
            ModeloEmailNotaService Modelos(ApplicationUser quem) =>
                new(db, tenantAtual, new UsuarioFalso(quem.Id), auditoria, new EmailQueueService(db, fila), plataforma,
                    sp.GetRequiredService<NfseSaaS.Application.UseCases.Recursos.IRecursosConta>());
            var modelosAdmin = Modelos(admin);
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), auditoria, modelosAdmin);
            var lote = new EnvioEmailNfseService(db, enviar, modelosAdmin);

            // Conta nova: ganha o modelo "Padrão" com o texto do sistema.
            var inicial = await modelosAdmin.ListarAsync(CancellationToken.None);
            var padrao = Assert.Single(inicial.Modelos);
            Assert.True(padrao.Padrao);
            Assert.Equal(RegrasModeloEmailNota.NomeModeloInicial, padrao.Nome);
            Assert.Equal(RegrasModeloEmailNota.AssuntoPadrao, padrao.Assunto);
            Assert.True(inicial.PodeAlterar);
            Assert.False(inicial.PodeOcultarDivulgacao); // perfil Empresa

            // Só o administrador grava; nome repetido e e-mail inválido barram.
            await Assert.ThrowsAsync<RegraNegocioException>(() => Modelos(comum).CriarAsync(
                new SalvarModeloEmailNotaRequest("X", "A", "B", null, null, false), CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => modelosAdmin.CriarAsync(
                new SalvarModeloEmailNotaRequest("padrão", "A", "B", null, null, false), CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => modelosAdmin.CriarAsync(
                new SalvarModeloEmailNotaRequest("Y", "A", "B", "sem-arroba", null, false), CancellationToken.None));

            // Padrão da conta com o exemplo do Carlos e cópia oculta fixa.
            await modelosAdmin.AtualizarAsync(padrao.Id, new SalvarModeloEmailNotaRequest("Padrão",
                "Nota [Número da nota] de [Mês por extenso]",
                "Você está recebendo a nota [Número da nota], referente ao mês [Mês por extenso], pelo site micoempresa da empresa [Empresa].",
                null, "escritorio@contab.com", OcultarDivulgacao: true), CancellationToken.None);

            // Modelo "Beta" para a empresa B e "Corporativo" para um cliente.
            var beta = await modelosAdmin.CriarAsync(new SalvarModeloEmailNotaRequest("Beta",
                "Beta: nota [Número da nota]", "Oi [Cliente], segue a nota.", "financeiro@beta.com", null, false), CancellationToken.None);
            var corporativo = await modelosAdmin.DuplicarAsync(beta, CancellationToken.None);
            await modelosAdmin.AtualizarAsync(corporativo, new SalvarModeloEmailNotaRequest("Corporativo",
                "Pedido de serviço — nota [Número da nota]", "Prezados, segue a nota do mês [Competência].", null, null, false), CancellationToken.None);
            await modelosAdmin.DefinirDaEmpresaAsync(empresaB.Id, new DefinirModeloEmailNotaRequest(beta), CancellationToken.None);
            await modelosAdmin.DefinirDoClienteAsync(comEmail.Id, new DefinirModeloEmailNotaRequest(corporativo), CancellationToken.None);

            var lista = await modelosAdmin.ListarAsync(CancellationToken.None);
            Assert.Equal(new[] { "Padrão", "Beta", "Corporativo" }, lista.Modelos.Select(m => m.Nome));
            Assert.Equal(new[] { "Beta LTDA" }, lista.Modelos.Single(m => m.Id == beta).Empresas);
            Assert.Equal(1, lista.Modelos.Single(m => m.Id == corporativo).Clientes);
            Assert.False(lista.Modelos.Single(m => m.Nome == "Padrão").OcultarDivulgacao); // só no plano Escritório

            // Prévia do modal: o modelo do cliente vence o da empresa e o padrão.
            var previa = await lote.PreviaAsync(nota1.Id, null, CancellationToken.None);
            Assert.Equal(corporativo, previa.ModeloId);
            Assert.Equal(3, previa.Modelos.Count);
            Assert.Equal("Pedido de serviço — nota 101", previa.Assunto);
            Assert.Equal("contato@alfa.com", previa.ResponderPara);
            Assert.Null(previa.CopiaOcultaPara);

            // Trocar o modelo no modal refaz o texto (e a cópia oculta vem dele).
            var previaPadrao = await lote.PreviaAsync(nota1.Id, padrao.Id, CancellationToken.None);
            Assert.Equal("Nota 101 de novembro de 2026", previaPadrao.Assunto);
            Assert.Contains("pelo site micoempresa da empresa Alfa.", previaPadrao.Mensagem);
            Assert.Equal("escritorio@contab.com", previaPadrao.CopiaOcultaPara);
            Assert.Equal("Cliente sem e-mail no cadastro.", (await lote.PreviaAsync(nota2.Id, null, CancellationToken.None)).Impedimento);

            // Texto do modal + modelo escolhido: só neste envio.
            await enviar.ExecutarAsync(nota1.Id, automatico: false,
                new TextoEmailNota("Assunto meu [Número da nota]", "Segue com o boleto.", padrao.Id), CancellationToken.None);
            var log1 = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.NfseId == nota1.Id);
            Assert.Equal("Assunto meu 101", log1.Assunto);
            Assert.Contains("Segue com o boleto.", log1.CorpoHtml);
            Assert.Equal("contato@alfa.com", log1.ResponderPara);
            Assert.Equal("escritorio@contab.com", log1.CopiaOcultaFixa);

            // Lote: 4 selecionadas → 2 enviáveis (nota2 sem e-mail, nota3 rejeitada), 1 delas já enviada.
            var resumo = await lote.PreviaLoteAsync(new EnvioEmailLoteRequest(new[] { nota1.Id, nota2.Id, nota3.Id, nota4.Id }), CancellationToken.None);
            Assert.Equal(new PreviaEnvioEmailLoteResponse(4, 2, 1, 1, 1, 2), resumo);

            var resultado = await lote.EnviarLoteAsync(new EnvioEmailLoteRequest(new[] { nota1.Id, nota2.Id, nota3.Id, nota4.Id }), CancellationToken.None);
            Assert.Equal(1, resultado.Enfileiradas); // nota4; nota1 já enviada
            Assert.Equal(1, resultado.Puladas);
            Assert.Equal(2, resultado.NaoEnviadas.Count);

            var log4 = await db.EmailLogs.AsNoTracking().SingleAsync(e => e.NfseId == nota4.Id);
            Assert.Equal("Beta: nota 104", log4.Assunto);
            Assert.Contains("Oi Cliente B", log4.CorpoHtml);
            Assert.Equal("financeiro@beta.com", log4.ResponderPara);
            Assert.Null(log4.CopiaOcultaFixa);

            // Sem pular: manda de novo a nota1 (agora com o modelo do cliente).
            var deNovo = await lote.EnviarLoteAsync(new EnvioEmailLoteRequest(new[] { nota1.Id }, PularJaEnviadas: false), CancellationToken.None);
            Assert.Equal(1, deNovo.Enfileiradas);
            Assert.Contains(await db.EmailLogs.AsNoTracking().Where(e => e.NfseId == nota1.Id).Select(e => e.Assunto).ToListAsync(),
                a => a == "Pedido de serviço — nota 101");

            // Lista de notas: envelope com o último envio.
            var notas = await new ListarNfseUseCase(db).ExecutarAsync(new ListarNfseRequest { EmpresaId = empresaA.Id }, CancellationToken.None);
            Assert.Equal(StatusEmailLog.Pendente, notas.Items.Single(n => n.Id == nota1.Id).EmailUltimoStatus);
            Assert.Null(notas.Items.Single(n => n.Id == nota2.Id).EmailUltimoStatus);

            // Padrão não sai; outro vira padrão; excluir devolve quem usava ao padrão.
            await Assert.ThrowsAsync<RegraNegocioException>(() => modelosAdmin.ExcluirAsync(padrao.Id, CancellationToken.None));
            await modelosAdmin.DefinirPadraoAsync(beta, CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Equal(beta, (await db.ModelosEmailNota.SingleAsync(m => m.Padrao)).Id);
            await modelosAdmin.ExcluirAsync(corporativo, CancellationToken.None);
            db.ChangeTracker.Clear();
            Assert.Null((await db.Clientes.AsNoTracking().SingleAsync(c => c.Id == comEmail.Id)).ModeloEmailNotaId);
            Assert.Equal(beta, (await lote.PreviaAsync(nota1.Id, null, CancellationToken.None)).ModeloId);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.TenantIdOverrideDeSistema = null;
            await db.EmailLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.ModelosEmailNota.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.NotasFiscais.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Clientes.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Users.Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Grupos.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Empresas.IgnoreQueryFilters().Where(x => x.TenantId == conta.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(x => x.Id == conta.Id).ExecuteDeleteAsync();
        }
    }
}
