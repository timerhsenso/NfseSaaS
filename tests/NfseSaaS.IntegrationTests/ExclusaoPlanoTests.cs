using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Domain.Cobranca;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Cobranca;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Excluir plano (Plataforma › Planos): código no e-mail do operador, só
/// plano nunca assinado e que não seja inicial; apaga o histórico de preços
/// junto e deixa o registro na Auditoria do operador.
/// </summary>
[Collection(AplicacaoEmMemoriaCollection.Nome)]
public class ExclusaoPlanoTests
{
    private readonly AplicacaoTesteFactory _factory;

    public ExclusaoPlanoTests(AplicacaoTesteFactory factory)
    {
        _factory = factory;
    }

    private sealed class EmailFalso : IEmailSender
    {
        public List<(string Para, string Corpo)> Enviados { get; } = new();

        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, CancellationToken cancellationToken)
        {
            Enviados.Add((destinatarioEmail, corpoHtml));
            return Task.FromResult(new ResultadoEnvioEmail(true, null));
        }

        public Task<ResultadoEnvioEmail> EnviarAsync(string destinatarioEmail, string assunto, string corpoHtml, IReadOnlyCollection<AnexoEmail> anexos, CancellationToken cancellationToken) =>
            EnviarAsync(destinatarioEmail, assunto, corpoHtml, cancellationToken);

        public string UltimoCodigo() => Regex.Match(Enviados[^1].Corpo, @">(\d{6})<").Groups[1].Value;
    }

    private static ExclusaoPlanoService Servico(IServiceProvider sp, IEmailSender email, IMemoryCache cache) => new(
        sp.GetRequiredService<AppDbContext>(),
        cache,
        email,
        sp.GetRequiredService<IAuditLogWriter>(),
        NullLogger<ExclusaoPlanoService>.Instance);

    [Fact]
    public async Task Plano_nunca_assinado_sai_com_o_codigo_certo_e_plano_assinado_ou_inicial_nao()
    {
        using var escopo = _factory.Services.CreateScope();
        var sp = escopo.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var admin = sp.GetRequiredService<IAdministrarPlanosUseCase>();

        var s = Guid.NewGuid().ToString("N");
        var operador = new Tenant { Documento = s[..14], RazaoSocial = "Operador " + s };
        var assinante = new Tenant { Documento = s[14..28], RazaoSocial = "Assinante " + s };
        db.Tenants.AddRange(operador, assinante);
        var usuario = new ApplicationUser { TenantId = operador.Id, UserName = $"op{s}@teste.com", Email = $"op{s}@teste.com" };
        db.Users.Add(usuario);
        await db.SaveChangesAsync();
        sp.GetRequiredService<ICurrentTenant>().DefinirTenantIdDeSistema(operador.Id);

        var email = new EmailFalso();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var servico = Servico(sp, email, cache);
        ISolicitarExclusaoPlanoUseCase solicitar = servico;
        IConfirmarExclusaoPlanoUseCase confirmar = servico;

        SalvarPlanoRequest Novo(string nome) => new(nome, "teste", PublicoPlano.Escritorios, false, true, 5, null, false, 999, 100m, 80m);
        var livre = await admin.CriarAsync(Novo("Livre " + s[..6]), usuario.Id, CancellationToken.None);
        var assinado = await admin.CriarAsync(Novo("Assinado " + s[..6]), usuario.Id, CancellationToken.None);
        // Assinatura cancelada também prende o plano (ela guarda o código).
        db.Assinaturas.Add(new Assinatura { TenantId = assinante.Id, Plano = assinado.Codigo, Status = Domain.Enums.StatusAssinatura.Cancelada });
        await db.SaveChangesAsync();

        try
        {
            var lista = await admin.ListarAsync(CancellationToken.None);
            Assert.True(lista.Single(p => p.Id == livre.Id).PodeExcluir);
            Assert.False(lista.Single(p => p.Id == assinado.Id).PodeExcluir);
            Assert.False(lista.Single(p => p.Codigo == Planos.CodigoEscritorio10).PodeExcluir);

            // Plano assinado e plano inicial: recusados já no pedido do código, sem e-mail.
            Assert.Contains("já foi assinado", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                solicitar.ExecutarAsync(assinado.Id, usuario.Id, CancellationToken.None))).Message);
            var inicial = lista.Single(p => p.Codigo == Planos.CodigoMico);
            Assert.Contains("iniciais", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                solicitar.ExecutarAsync(inicial.Id, usuario.Id, CancellationToken.None))).Message);
            Assert.Empty(email.Enviados);

            // Sem pedir o código não exclui.
            await Assert.ThrowsAsync<RegraNegocioException>(() => confirmar.ExecutarAsync(livre.Id, usuario.Id, "123456", CancellationToken.None));

            var pedido = await solicitar.ExecutarAsync(livre.Id, usuario.Id, CancellationToken.None);
            Assert.Equal(usuario.Email, Assert.Single(email.Enviados).Para);
            Assert.Equal(1, pedido.Precos);
            var codigo = email.UltimoCodigo();
            var errado = codigo == "000000" ? "111111" : "000000";

            Assert.Contains("Restam", (await Assert.ThrowsAsync<RegraNegocioException>(() =>
                confirmar.ExecutarAsync(livre.Id, usuario.Id, errado, CancellationToken.None))).Message);
            Assert.True(await db.Planos.AnyAsync(p => p.Id == livre.Id));

            await confirmar.ExecutarAsync(livre.Id, usuario.Id, codigo, CancellationToken.None);

            Assert.False(await db.Planos.AnyAsync(p => p.Id == livre.Id));
            Assert.False(await db.PrecosPlanos.AnyAsync(p => p.PlanoId == livre.Id));
            Assert.True(await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.TenantId == operador.Id && a.Operacao == "ExcluirPlano" && a.EntidadeId == livre.Id));

            // Código de uso único.
            await Assert.ThrowsAnyAsync<Exception>(() => confirmar.ExecutarAsync(livre.Id, usuario.Id, codigo, CancellationToken.None));
        }
        finally
        {
            await db.Assinaturas.Where(a => a.TenantId == assinante.Id).ExecuteDeleteAsync();
            var ids = new[] { livre.Id, assinado.Id };
            await db.PrecosPlanos.Where(p => ids.Contains(p.PlanoId)).ExecuteDeleteAsync();
            await db.Planos.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync();
            await db.AuditLogs.IgnoreQueryFilters().Where(a => a.TenantId == operador.Id).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == usuario.Id).ExecuteDeleteAsync();
            await db.Tenants.Where(t => t.Id == operador.Id || t.Id == assinante.Id).ExecuteDeleteAsync();
        }
    }
}
