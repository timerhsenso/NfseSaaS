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
/// E-mail da nota com o banco real: modelo da conta e da empresa, texto
/// do modal só para um envio, responder-para e cópia oculta fixa, e o
/// envio de várias notas (resumo, sem e-mail, não autorizada, já enviada).
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
    public async Task Modelo_da_conta_e_da_empresa_texto_do_modal_e_envio_em_lote()
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
                new(db, tenantAtual, new UsuarioFalso(quem.Id), auditoria, new EmailQueueService(db, fila), plataforma);
            var modelosAdmin = Modelos(admin);
            var enviar = new EnviarNfsePorEmailUseCase(db, new EmailQueueService(db, fila), auditoria, modelosAdmin);
            var lote = new EnvioEmailNfseService(db, enviar, modelosAdmin);

            // Sem modelo salvo: texto padrão, respostas para o e-mail da empresa.
            var inicial = await modelosAdmin.ObterAsync(null, CancellationToken.None);
            Assert.False(inicial.Personalizado);
            Assert.Equal(RegrasModeloEmailNota.AssuntoPadrao, inicial.Assunto);
            Assert.True(inicial.PodeAlterar);
            Assert.False(inicial.PodeOcultarDivulgacao); // perfil Empresa
            Assert.Equal(2, inicial.Empresas.Count);

            // Só o administrador grava.
            await Assert.ThrowsAsync<RegraNegocioException>(() => Modelos(comum).SalvarAsync(
                new SalvarModeloEmailNotaRequest(null, true, "X", "Y", null, null, false), CancellationToken.None));
            await Assert.ThrowsAsync<RegraNegocioException>(() => modelosAdmin.SalvarAsync(
                new SalvarModeloEmailNotaRequest(null, true, "X", "Y", "sem-arroba", null, false), CancellationToken.None));

            // Modelo da conta (o exemplo do Carlos) com cópia oculta fixa.
            var salvo = await modelosAdmin.SalvarAsync(new SalvarModeloEmailNotaRequest(null, true,
                "Nota [Número da nota] de [Mês por extenso]",
                "Você está recebendo a nota [Número da nota], referente ao mês [Mês por extenso], pelo site micoempresa da empresa [Empresa].",
                null, "escritorio@contab.com", OcultarDivulgacao: true), CancellationToken.None);
            Assert.True(salvo.Personalizado);
            Assert.False(salvo.OcultarDivulgacao); // só no plano Escritório

            // Empresa B personalizada.
            await modelosAdmin.SalvarAsync(new SalvarModeloEmailNotaRequest(empresaB.Id, true,
                "Beta: nota [Número da nota]", "Oi [Cliente], segue a nota.", "financeiro@beta.com", null, false), CancellationToken.None);
            Assert.True((await modelosAdmin.ObterAsync(empresaB.Id, CancellationToken.None)).Personalizado);
            Assert.False((await modelosAdmin.ObterAsync(empresaA.Id, CancellationToken.None)).Personalizado);

            // Prévia do modal: texto já preenchido.
            var previa = await lote.PreviaAsync(nota1.Id, CancellationToken.None);
            Assert.Equal("cliente@teste.com", previa.Destinatario);
            Assert.Equal("Nota 101 de novembro de 2026", previa.Assunto);
            Assert.Contains("pelo site micoempresa da empresa Alfa.", previa.Mensagem);
            Assert.Equal("contato@alfa.com", previa.ResponderPara);
            Assert.Equal("escritorio@contab.com", previa.CopiaOcultaPara);
            Assert.Null(previa.Impedimento);
            Assert.Equal("Cliente sem e-mail no cadastro.", (await lote.PreviaAsync(nota2.Id, CancellationToken.None)).Impedimento);

            // Texto do modal: só neste envio.
            await enviar.ExecutarAsync(nota1.Id, automatico: false, new TextoEmailNota("Assunto meu [Número da nota]", "Segue com o boleto."), CancellationToken.None);
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

            // Sem pular: manda de novo a nota1.
            var deNovo = await lote.EnviarLoteAsync(new EnvioEmailLoteRequest(new[] { nota1.Id }, PularJaEnviadas: false), CancellationToken.None);
            Assert.Equal(1, deNovo.Enfileiradas);

            // Lista: envelope com o último envio.
            var lista = await new ListarNfseUseCase(db).ExecutarAsync(new ListarNfseRequest { EmpresaId = empresaA.Id }, CancellationToken.None);
            Assert.Equal(StatusEmailLog.Pendente, lista.Items.Single(n => n.Id == nota1.Id).EmailUltimoStatus);
            Assert.Null(lista.Items.Single(n => n.Id == nota2.Id).EmailUltimoStatus);

            // Empresa B volta ao modelo da conta.
            var voltou = await modelosAdmin.SalvarAsync(new SalvarModeloEmailNotaRequest(empresaB.Id, false, null, null, null, null, false), CancellationToken.None);
            Assert.False(voltou.Personalizado);
            Assert.Equal("Nota [Número da nota] de [Mês por extenso]", voltou.Assunto);
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
