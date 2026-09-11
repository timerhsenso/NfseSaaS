using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.AuditLogs;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Application.UseCases.HistoricoContrato;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Infrastructure.Documents;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Application.UseCases.Exportacoes;
using NfseSaaS.Application.UseCases.Consultas;
using NfseSaaS.Infrastructure.Consultas;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NfseEventos;
using NfseSaaS.Application.UseCases.ReajustesContrato;
using NfseSaaS.Application.UseCases.Servicos;
using NfseSaaS.Application.UseCases.SincronizacaoSefin;
using NfseSaaS.Infrastructure.Auditing;
using NfseSaaS.Infrastructure.Certificates;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Identity;
using NfseSaaS.Infrastructure.MultiTenancy;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Infrastructure.UseCases;
using NfseSaaS.Nacional.Abstractions;

namespace NfseSaaS.Infrastructure;

/// <summary>
/// Ponto único de registro da camada Infrastructure no container de DI.
/// Mantém o Program.cs do Web enxuto (ver Requisito 16 da especificação).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Licença Community: gratuita só se a receita anual de QUEM USA
        // este software for menor que USD 1 milhão — confirme se aplica
        // ao seu caso antes de ir pra produção
        // (https://www.questpdf.com/license/). Se não aplicar, é preciso
        // licença comercial da QuestPDF ou trocar de biblioteca de PDF.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection não configurada. " +
                "Em desenvolvimento, defina via 'dotnet user-secrets set \"ConnectionStrings:DefaultConnection\" \"...\"'.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<INfseEventoWriter, NfseEventoWriter>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Regras mínimas de senha para esta fase. Serão revisadas
                // conforme requisitos de segurança do produto evoluírem.
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

        // Por padrão, o cookie do Identity REDIRECIONA (302) para uma
        // página de login em caso de falha de autenticação — faz sentido
        // para MVC, mas não para as rotas /api (que não têm essa página e
        // esperam um 401 puro, como qualquer API JSON).
        services.ConfigureApplicationCookie(options =>
        {
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        AddCertificateStorage(services, configuration);
        AddDocumentStorage(services, configuration);

        // Casos de uso: implementações reais aqui (não em Application),
        // porque tocam EF Core diretamente — sem repository genérico nem
        // UnitOfWork artificial em cima do EF Core.
        services.AddScoped<ICadastrarEmpresaUseCase, CadastrarEmpresaUseCase>();
        services.AddScoped<IListarEmpresasUseCase, ListarEmpresasUseCase>();
        services.AddScoped<IObterEmpresaPorIdUseCase, ObterEmpresaPorIdUseCase>();
        services.AddScoped<IAtualizarEmpresaUseCase, AtualizarEmpresaUseCase>();
        services.AddScoped<IDesativarEmpresaUseCase, DesativarEmpresaUseCase>();
        services.AddScoped<IReativarEmpresaUseCase, ReativarEmpresaUseCase>();
        services.AddScoped<IExcluirEmpresaUseCase, ExcluirEmpresaUseCase>();
        services.AddScoped<IAlterarAmbienteEmpresaUseCase, AlterarAmbienteEmpresaUseCase>();

        services.AddScoped<ICadastrarClienteUseCase, CadastrarClienteUseCase>();
        services.AddScoped<IListarClientesUseCase, ListarClientesUseCase>();
        services.AddScoped<IObterClientePorIdUseCase, ObterClientePorIdUseCase>();
        services.AddScoped<IAtualizarClienteUseCase, AtualizarClienteUseCase>();
        services.AddScoped<IDesativarClienteUseCase, DesativarClienteUseCase>();
        services.AddScoped<IReativarClienteUseCase, ReativarClienteUseCase>();
        services.AddScoped<IExcluirClienteUseCase, ExcluirClienteUseCase>();

        services.AddScoped<ICadastrarServicoUseCase, CadastrarServicoUseCase>();
        services.AddScoped<IListarServicosUseCase, ListarServicosUseCase>();
        services.AddScoped<IObterServicoPorIdUseCase, ObterServicoPorIdUseCase>();
        services.AddScoped<IAtualizarServicoUseCase, AtualizarServicoUseCase>();
        services.AddScoped<IDesativarServicoUseCase, DesativarServicoUseCase>();
        services.AddScoped<IReativarServicoUseCase, ReativarServicoUseCase>();
        services.AddScoped<IExcluirServicoUseCase, ExcluirServicoUseCase>();

        services.AddScoped<ICadastrarContratoUseCase, CadastrarContratoUseCase>();
        services.AddScoped<IListarContratosUseCase, ListarContratosUseCase>();
        services.AddScoped<IObterContratoPorIdUseCase, ObterContratoPorIdUseCase>();
        services.AddScoped<IAtualizarContratoUseCase, AtualizarContratoUseCase>();
        services.AddScoped<IDesativarContratoUseCase, DesativarContratoUseCase>();
        services.AddScoped<IReativarContratoUseCase, ReativarContratoUseCase>();
        services.AddScoped<IExcluirContratoUseCase, ExcluirContratoUseCase>();

        services.AddScoped<IRegistrarReajusteUseCase, RegistrarReajusteUseCase>();
        services.AddScoped<IListarReajustesUseCase, ListarReajustesUseCase>();

        services.AddScoped<IUploadDocumentoContratoUseCase, UploadDocumentoContratoUseCase>();
        services.AddScoped<IListarDocumentosContratoUseCase, ListarDocumentosContratoUseCase>();
        services.AddScoped<IExcluirDocumentoContratoUseCase, ExcluirDocumentoContratoUseCase>();
        services.AddScoped<IObterDocumentoContratoParaDownloadUseCase, ObterDocumentoContratoParaDownloadUseCase>();

        services.AddScoped<IObterHistoricoContratoUseCase, ObterHistoricoContratoUseCase>();

        services.AddScoped<IEmitirNfseUseCase, EmitirNfseUseCase>();
        services.AddScoped<IListarNfseUseCase, ListarNfseUseCase>();
        services.AddScoped<IObterNfsePorIdUseCase, ObterNfsePorIdUseCase>();

        services.AddScoped<IListarCandidatosNotaMensalUseCase, ListarCandidatosNotaMensalUseCase>();
        services.AddScoped<IEmitirNotaMensalLoteUseCase, EmitirNotaMensalLoteUseCase>();
        services.AddScoped<ICancelarNfseUseCase, CancelarNfseUseCase>();
        services.AddScoped<IListarEventosDaNfseUseCase, ListarEventosDaNfseUseCase>();
        services.AddScoped<IObterSnapshotFiscalDaNfseUseCase, ObterSnapshotFiscalDaNfseUseCase>();
        services.AddScoped<IObterDanfsePdfUseCase, ObterDanfsePdfUseCase>();
        services.AddScoped<IGerarDanfsePdfLoteUseCase, GerarDanfsePdfLoteUseCase>();
        services.AddScoped<ISincronizarNotasDaSefinUseCase, SincronizarNotasDaSefinUseCase>();
        services.AddScoped<IRegistrarExportacaoUseCase, RegistrarExportacaoUseCase>();
        services.AddHttpClient("BrasilApi");
        services.AddScoped<IConsultarCnpjUseCase, BrasilApiCnpjClient>();

        services.AddScoped<IListarAuditLogsUseCase, ListarAuditLogsUseCase>();
        services.AddScoped<IObterAuditLogPorIdUseCase, ObterAuditLogPorIdUseCase>();

        services.AddScoped<IEnviarCertificadoUseCase, EnviarCertificadoUseCase>();
        services.AddScoped<IObterStatusCertificadoUseCase, ObterStatusCertificadoUseCase>();
        services.AddScoped<ITestarConexaoCertificadoUseCase, TestarConexaoCertificadoUseCase>();

        return services;
    }

    /// <summary>
    /// Armazenamento de certificados .pfx por Empresa: arquivo criptografado
    /// em disco (fora do wwwroot, fora do Git), protegido com ASP.NET Core
    /// Data Protection. No Windows, as chaves de proteção em si são
    /// protegidas via DPAPI em nível de MÁQUINA (não por usuário) — decisão
    /// deliberada porque a identidade do App Pool do IIS pode não ter
    /// perfil de usuário carregado; qualquer processo com acesso
    /// administrativo a esta máquina consegue descriptografar, o que é uma
    /// premissa aceitável para um servidor único on-premises como este.
    /// </summary>
    private static void AddCertificateStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CertificateStorageOptions>(configuration.GetSection(CertificateStorageOptions.SectionName));

        var certOptions = configuration.GetSection(CertificateStorageOptions.SectionName).Get<CertificateStorageOptions>()
            ?? new CertificateStorageOptions();

        var dataProtectionBuilder = services.AddDataProtection().SetApplicationName("NfseSaaS");

        if (!string.IsNullOrWhiteSpace(certOptions.BasePath))
        {
            var keysPath = Path.Combine(certOptions.BasePath, "dp-keys");
            Directory.CreateDirectory(keysPath);
            dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        if (OperatingSystem.IsWindows())
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);

        services.AddScoped<CertificateFileStore>();
        services.AddScoped<ICertificateProvider, FileCertificateProvider>();
    }

    /// <summary>Raiz de disco pros documentos de Contrato — separada da raiz de certificados, sem criptografia (decisão do usuário).</summary>
    private static void AddDocumentStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentStorageOptions>(configuration.GetSection(DocumentStorageOptions.SectionName));
        services.AddScoped<ContratoDocumentoFileStore>();
    }
}
