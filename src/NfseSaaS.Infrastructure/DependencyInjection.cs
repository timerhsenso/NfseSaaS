using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.AuditLogs;
using NfseSaaS.Application.UseCases.AutomacaoNotaMensal;
using NfseSaaS.Application.UseCases.Catalogos;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Application.UseCases.Clientes;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Application.UseCases.HistoricoContrato;
using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using NfseSaaS.Application.UseCases.IndicesEconomicos;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Application.UseCases.Plataforma;
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
using NfseSaaS.Infrastructure.IndicesEconomicos;
using NfseSaaS.Infrastructure.Jobs;
using NfseSaaS.Infrastructure.Monitoramento;
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

        // Motor da automação de Nota Mensal — mesmo Postgres do resto da
        // app (a mesma connectionString de cima), schema próprio
        // gerenciado pelo Hangfire, sem precisar de banco separado.
        // AddHangfireServer roda o worker que processa os jobs
        // recorrentes; o dashboard (/hangfire) é mapeado no Program.cs
        // (é middleware de pipeline, não registro de DI).
        // AlertaFalhaJobFilter: job que falha de vez (depois das novas
        // tentativas) vira Error no log -> e-mail ao operador.
        services.AddHangfire((provider, config) => config
            .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString))
            .UseFilter(new AlertaFalhaJobFilter(
                provider.GetRequiredService<ILoggerFactory>())));
        services.AddHangfireServer();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAceiteTermosService, NfseSaaS.Infrastructure.Legal.AceiteTermosService>();
        services.AddScoped<NfseSaaS.Infrastructure.Legal.AvisoNovosTermosService>();
        services.AddScoped<IGrupoProvisionamentoService, GrupoProvisionamentoService>();
        services.AddScoped<INfseEventoWriter, NfseEventoWriter>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<NfseSaaS.Application.Common.SuporteOptions>(configuration.GetSection(NfseSaaS.Application.Common.SuporteOptions.SectionName));
        services.Configure<PlataformaOptions>(configuration.GetSection(PlataformaOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<IBackgroundEmailQueue, BackgroundEmailQueue>();
        services.AddScoped<IEmailQueueService, EmailQueueService>();
        // Alertas ao operador da plataforma (erros do log e jobs que falharam):
        // a fila recebe do sink do Serilog (Web) e o hosted service manda o e-mail.
        services.AddSingleton<AlertaOperadorFila>();
        services.AddHostedService<AlertaOperadorHostedService>();

        // Anexos (PDF e XML) do e-mail da nota ao tomador, montados na hora do envio.
        services.AddScoped<AnexosNfseEmail>();
        services.AddScoped<AvisoReajustesAniversario>();
        services.AddScoped<IEnviarNfsePorEmailUseCase, EnviarNfsePorEmailUseCase>();
        services.AddScoped<IEnvioEmailNfseUseCase, EnvioEmailNfseService>();
        services.AddScoped<NfseSaaS.Application.UseCases.EmailNota.IModeloEmailNotaUseCase, ModeloEmailNotaService>();
        services.AddHostedService<EmailDispatchHostedService>();

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                // Senha forte: 8+ caracteres, maiúscula, minúscula, número
                // e símbolo — explícito aqui de propósito (mesmo que
                // coincida com o default do Identity) pra não depender de
                // ninguém saber de cor qual é esse default.
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;

                // Bloqueio após tentativa errada — 5 tentativas, 15
                // minutos bloqueado. Só tem efeito de verdade combinado
                // com lockoutOnFailure:true na chamada de
                // PasswordSignInAsync (ver AuthController.Login).
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddErrorDescriber<IdentityErrorDescriberPtBr>()
            .AddSignInManager<SignInManagerComTenant>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>();

        // A cada 1 minuto (padrão do Identity: 30) o cookie é revalidado
        // contra o banco: o SecurityStamp do usuário é conferido e os
        // Claims são regerados pelo ApplicationUserClaimsPrincipalFactory.
        // Efeito: bloquear um usuário (que troca o SecurityStamp) derruba a
        // sessão dele em até 1 minuto, e mudanças de Grupo/permissão passam
        // a valer em até 1 minuto sem precisar relogar. Custo: uma consulta
        // por usuário ativo por minuto — irrelevante neste porte.
        services.Configure<SecurityStampValidatorOptions>(options =>
            options.ValidationInterval = TimeSpan.FromMinutes(1));

        // Verificação em duas etapas (B3): "não pedir de novo neste navegador"
        // dura 14 dias (cookie do Identity, invalidado ao desligar ou trocar
        // o SecurityStamp); a etapa do código dura 5 minutos (padrão).
        services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            IdentityConstants.TwoFactorRememberMeScheme, options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromDays(NfseSaaS.Application.UseCases.Seguranca.RegrasDuploFator.DiasLembrarNavegador);
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });
        services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
            IdentityConstants.TwoFactorUserIdScheme, options =>
                options.ExpireTimeSpan = TimeSpan.FromMinutes(NfseSaaS.Application.UseCases.Seguranca.RegrasDuploFator.MinutosParaDigitar));

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
        services.AddScoped<IObterDisponibilidadeClienteUseCase, ObterDisponibilidadeClienteUseCase>();
        services.AddScoped<IObterDadosSubstituicaoUseCase, ObterDadosSubstituicaoUseCase>();

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
        services.AddScoped<IExcluirContratoUseCase, ExcluirContratoUseCase>();

        services.AddScoped<IRegistrarReajusteUseCase, RegistrarReajusteUseCase>();
        services.AddScoped<ISimularReajusteContratoUseCase, SimularReajusteContratoUseCase>();
        services.AddScoped<IObterProximoNumeroContratoUseCase, ObterProximoNumeroContratoUseCase>();
        services.AddScoped<NfseSaaS.Application.UseCases.Conta.IDadosContaUseCase, DadosContaService>();
        services.AddScoped<IListarHistoricoValoresContratoUseCase, ListarHistoricoValoresContratoUseCase>();
        services.AddScoped<IGerarReajustesAniversarioUseCase, GerarReajustesAniversarioUseCase>();
        services.AddScoped<IDecidirReajusteUseCase, DecidirReajusteUseCase>();
        services.AddScoped<IDesfazerReajusteUseCase, DesfazerReajusteUseCase>();
        services.AddScoped<IGerarReajustesAniversarioJob, GerarReajustesAniversarioJob>();
        // Aviso diário de certificado digital vencendo (30/15/7 dias e vencido).
        services.AddScoped<IAvisarCertificadosVencendoJob, AvisarCertificadosVencendoJob>();
        services.AddScoped<IAvisarCertificadoVencendoUseCase, AvisarCertificadoVencendoUseCase>();

        services.AddScoped<IUploadDocumentoContratoUseCase, UploadDocumentoContratoUseCase>();
        services.AddScoped<IListarDocumentosContratoUseCase, ListarDocumentosContratoUseCase>();
        services.AddScoped<IExcluirDocumentoContratoUseCase, ExcluirDocumentoContratoUseCase>();
        services.AddScoped<IObterDocumentoContratoParaDownloadUseCase, ObterDocumentoContratoParaDownloadUseCase>();

        services.AddScoped<IObterHistoricoContratoUseCase, ObterHistoricoContratoUseCase>();

        services.AddScoped<IEmitirNfseUseCase, EmitirNfseUseCase>();
        services.AddScoped<ISimularRetencoesUseCase, SimularRetencoesUseCase>();
        services.AddScoped<RecursosContaService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Recursos.IRecursosConta>(sp => sp.GetRequiredService<RecursosContaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Recursos.IAdministrarRecursosUseCase>(sp => sp.GetRequiredService<RecursosContaService>());
        services.AddScoped<IListarNfseUseCase, ListarNfseUseCase>();
        services.AddScoped<IObterNfsePorIdUseCase, ObterNfsePorIdUseCase>();

        services.AddScoped<IListarCandidatosNotaMensalUseCase, ListarCandidatosNotaMensalUseCase>();
        services.AddScoped<IEmitirNotaMensalLoteUseCase, EmitirNotaMensalLoteUseCase>();
        services.AddScoped<ICancelarNfseUseCase, CancelarNfseUseCase>();
        services.AddScoped<IListarEventosDaNfseUseCase, ListarEventosDaNfseUseCase>();
        services.AddScoped<IObterSnapshotFiscalDaNfseUseCase, ObterSnapshotFiscalDaNfseUseCase>();
        services.AddScoped<IObterDanfsePdfUseCase, ObterDanfsePdfUseCase>();
        services.AddScoped<IObterXmlNfseUseCase, ObterXmlNfseUseCase>();
        services.AddScoped<IGerarDanfsePdfLoteUseCase, GerarDanfsePdfLoteUseCase>();
        services.AddScoped<ISincronizarNotasDaSefinUseCase, SincronizarNotasDaSefinUseCase>();
        services.AddScoped<IRegistrarExportacaoUseCase, RegistrarExportacaoUseCase>();
        services.AddHttpClient("BrasilApi");
        services.AddScoped<IConsultarCnpjUseCase, BrasilApiCnpjClient>();
        services.AddMemoryCache();
        services.AddHttpClient("ConsultaEndereco");
        services.AddScoped<ConsultaEnderecoClient>();
        services.AddScoped<IConsultarCepUseCase>(sp => sp.GetRequiredService<ConsultaEnderecoClient>());
        // Municípios (C9): tabela local, também para o nome pelo código (antes: API do IBGE).
        services.AddScoped<NfseSaaS.Infrastructure.UseCases.MunicipiosService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Municipios.IMunicipiosUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.UseCases.MunicipiosService>());
        services.AddScoped<IConsultarMunicipioUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.UseCases.MunicipiosService>());

        services.AddScoped<IBuscarCodigoTributacaoNacionalUseCase, BuscarCodigoTributacaoNacionalUseCase>();
        services.AddScoped<IBuscarCodigoNbsUseCase, BuscarCodigoNbsUseCase>();

        // Índices econômicos (IPCA, IGP-M...) — tabela global alimentada pela
        // API SGS do Banco Central; base do reajuste anual dos Contratos.
        services.Configure<IndicesEconomicosOptions>(configuration.GetSection(IndicesEconomicosOptions.SectionName));
        services.AddHttpClient<IFonteIndicesEconomicos, BancoCentralSgsClient>((sp, client) =>
        {
            var opcoes = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IndicesEconomicosOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(opcoes.TimeoutSeconds, 5));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NfseSaaS/1.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<IListarIndicesDisponiveisUseCase, ListarIndicesDisponiveisUseCase>();
        services.AddScoped<IListarIndicesEconomicosUseCase, ListarIndicesEconomicosUseCase>();
        services.AddScoped<ICalcularIndiceAcumuladoUseCase, CalcularIndiceAcumuladoUseCase>();
        services.AddScoped<ISalvarIndiceManualUseCase, SalvarIndiceManualUseCase>();
        services.AddScoped<IExcluirIndiceEconomicoUseCase, ExcluirIndiceEconomicoUseCase>();
        services.AddScoped<IImportarIndicesEconomicosUseCase, ImportarIndicesEconomicosUseCase>();
        services.AddScoped<IImportarIndicesEconomicosJob, ImportarIndicesEconomicosJob>();

        services.AddScoped<IObterConfiguracaoAutomacaoNotaMensalUseCase, ObterConfiguracaoAutomacaoNotaMensalUseCase>();
        services.AddScoped<IAtualizarConfiguracaoAutomacaoNotaMensalUseCase, AtualizarConfiguracaoAutomacaoNotaMensalUseCase>();
        services.AddScoped<IExecutarAutomacaoNotaMensalJob, ExecutarAutomacaoNotaMensalJob>();

        services.AddScoped<IListarAuditLogsUseCase, ListarAuditLogsUseCase>();

        services.AddScoped<IListarTenantsPlataformaUseCase, ListarTenantsPlataformaUseCase>();
        services.AddScoped<NfseSaaS.Application.UseCases.Onboarding.IObterProgressoOnboardingUseCase, ObterProgressoOnboardingUseCase>();
        // Cadastro em etapas: consulta pública do CNPJ, Empresa criada junto e confirmação de e-mail.
        services.AddScoped<NfseSaaS.Application.UseCases.Cadastro.IConsultarCnpjCadastroUseCase, ConsultarCnpjCadastroUseCase>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cadastro.ICriarEmpresaInicialService, CriarEmpresaInicialService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cadastro.IConfirmacaoEmailService, ConfirmacaoEmailService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.IObterIndicacaoUseCase, ObterIndicacaoUseCase>();

        // Cobrança (planos, teste grátis, Asaas). Desligada por padrão (Cobranca:Ativa=false).
        services.Configure<NfseSaaS.Infrastructure.Cobranca.CobrancaOptions>(configuration.GetSection(NfseSaaS.Infrastructure.Cobranca.CobrancaOptions.SectionName));
        services.Configure<NfseSaaS.Infrastructure.Cobranca.AsaasOptions>(configuration.GetSection(NfseSaaS.Infrastructure.Cobranca.AsaasOptions.SectionName));
        services.AddHttpClient<NfseSaaS.Application.UseCases.Cobranca.IGatewayCobranca, NfseSaaS.Infrastructure.Cobranca.AsaasClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.CatalogoPlanosService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.ICatalogoPlanos>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CatalogoPlanosService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IAdministrarPlanosUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CatalogoPlanosService>());
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.ExclusaoPlanoService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.ISolicitarExclusaoPlanoUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.ExclusaoPlanoService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IConfirmarExclusaoPlanoUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.ExclusaoPlanoService>());
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.CobrancaService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IVerificadorCobranca>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IObterAssinaturaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IAssinarUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.ICancelarAssinaturaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.ITrocarPlanoUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IReservaFundador>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.CobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IProcessarWebhookAsaasUseCase, NfseSaaS.Infrastructure.Cobranca.ProcessarWebhookAsaasUseCase>();
        // Conciliação diária com o Asaas (corrige o que o webhook deixou passar).
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IConciliarCobrancaUseCase, NfseSaaS.Infrastructure.Cobranca.ConciliacaoCobrancaService>();
        // Prêmio do "Indique e ganhe" (1 mês grátis), aplicado no Asaas por job.
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.PremioIndicacaoService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.IAplicarPremiosIndicacaoUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.PremioIndicacaoService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.IListarPremiosIndicacaoPlataformaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.PremioIndicacaoService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.IReabrirPremioIndicacaoUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.PremioIndicacaoService>());
        services.AddScoped<IAplicarPremiosIndicacaoJob, AplicarPremiosIndicacaoJob>();
        // Reajuste do preço de tabela para quem já assina (B2): operador agenda, job aplica.
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.ReajusteAssinaturaService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IReajusteAssinantesUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.ReajusteAssinaturaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IAplicarReajustesAssinaturaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.ReajusteAssinaturaService>());
        services.AddScoped<IAplicarReajustesAssinaturaJob, AplicarReajustesAssinaturaJob>();
        // Avisos de cobrança por e-mail (job diário + disparo pelo webhook) e faixa do painel.
        services.AddScoped<NfseSaaS.Infrastructure.Cobranca.AvisosCobrancaService>();
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IEnviarAvisosCobrancaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.AvisosCobrancaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.IObterFaixaCobrancaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.Cobranca.AvisosCobrancaService>());
        services.AddScoped<IAvisarCobrancaJob, AvisarCobrancaJob>();
        services.AddScoped<IConciliarCobrancaJob, ConciliarCobrancaJob>();
        services.AddScoped<IAvisarNovosTermosJob, AvisarNovosTermosJob>();
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.IConvidarPorEmailUseCase, NfseSaaS.Infrastructure.UseCases.ConvidarPorEmailUseCase>();
        // Retenção dos convites (B7): apaga convites e e-mails de convite após 12 meses.
        services.AddScoped<NfseSaaS.Application.UseCases.Indicacao.ILimparConvitesIndicacaoUseCase, NfseSaaS.Infrastructure.UseCases.LimparConvitesIndicacaoService>();
        services.AddScoped<ILimparConvitesIndicacaoJob, LimparConvitesIndicacaoJob>();
        // XML do mês (tela de notas): leve, sem limite de vezes.
        services.AddScoped<NfseSaaS.Application.UseCases.Nfse.IBaixarXmlMesUseCase, NfseSaaS.Infrastructure.UseCases.BaixarXmlMesUseCase>();
        // Exportação dos dados da conta (D1): tela "Meus dados" + job que monta o ZIP e limpeza diária.
        services.Configure<NfseSaaS.Infrastructure.DadosConta.ExportacaoDadosOptions>(configuration.GetSection(NfseSaaS.Infrastructure.DadosConta.ExportacaoDadosOptions.SectionName));
        services.AddScoped<NfseSaaS.Infrastructure.DadosConta.MontadorExportacaoDados>();
        services.AddScoped<NfseSaaS.Infrastructure.DadosConta.ExportacaoDadosService>();
        services.AddScoped<NfseSaaS.Application.UseCases.DadosConta.IExportacaoDadosUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.DadosConta.ExportacaoDadosService>());
        services.AddScoped<NfseSaaS.Application.UseCases.DadosConta.IGerarExportacaoDadosUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.DadosConta.ExportacaoDadosService>());
        services.AddScoped<IExportacaoDadosJob, ExportacaoDadosJob>();
        // Encerramento da conta (D2): pelo administrador ou pelo operador; expurgo diário após 90 dias.
        services.AddScoped<NfseSaaS.Infrastructure.DadosConta.EncerramentoContaService>();
        services.AddScoped<NfseSaaS.Application.UseCases.DadosConta.IEncerrarContaUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.DadosConta.EncerramentoContaService>());
        services.AddScoped<NfseSaaS.Application.UseCases.DadosConta.IExpurgarContasEncerradasUseCase>(sp => sp.GetRequiredService<NfseSaaS.Infrastructure.DadosConta.EncerramentoContaService>());
        services.AddScoped<IExpurgarContasEncerradasJob, ExpurgarContasEncerradasJob>();
        // Encerramento automático das contas canceladas (D2 parte 2): DESLIGADO sem Encerramento:AutomaticoAposCancelarDias.
        services.Configure<NfseSaaS.Infrastructure.DadosConta.EncerramentoOptions>(configuration.GetSection(NfseSaaS.Infrastructure.DadosConta.EncerramentoOptions.SectionName));
        services.AddScoped<NfseSaaS.Application.UseCases.DadosConta.IEncerrarContasCanceladasUseCase, NfseSaaS.Infrastructure.DadosConta.EncerramentoAutomaticoService>();
        services.AddScoped<IEncerrarContasCanceladasJob, EncerrarContasCanceladasJob>();
        // NFS-e da mensalidade (N1): DESLIGADA sem NotaMensalidade:EmpresaId e NotaMensalidade:ServicoId.
        services.Configure<NfseSaaS.Infrastructure.Cobranca.NotaMensalidadeOptions>(configuration.GetSection(NfseSaaS.Infrastructure.Cobranca.NotaMensalidadeOptions.SectionName));
        services.AddScoped<NfseSaaS.Application.UseCases.Cobranca.INotasMensalidadeUseCase, NfseSaaS.Infrastructure.Cobranca.NotasMensalidadeService>();
        services.AddScoped<IEmitirNotasMensalidadeJob, EmitirNotasMensalidadeJob>();
        services.AddScoped<NfseSaaS.Application.UseCases.Aparencia.IAparenciaUseCase, NfseSaaS.Infrastructure.UseCases.AparenciaService>();
        services.AddScoped<IAlterarBloqueioTenantUseCase, AlterarBloqueioTenantUseCase>();
        services.AddScoped<ICobrancaOperadorUseCase, NfseSaaS.Infrastructure.Cobranca.CobrancaOperadorService>();
        // Exclusão definitiva de conta pelo operador (código por e-mail).
        services.AddScoped<ExclusaoContaService>();
        services.AddScoped<ISolicitarExclusaoContaUseCase>(sp => sp.GetRequiredService<ExclusaoContaService>());
        services.AddScoped<IConfirmarExclusaoContaUseCase>(sp => sp.GetRequiredService<ExclusaoContaService>());
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

        var protecao = configuration.GetSection(ProtecaoChavesOptions.SectionName).Get<ProtecaoChavesOptions>()
            ?? new ProtecaoChavesOptions();

        var dataProtectionBuilder = services.AddDataProtection().SetApplicationName("NfseSaaS");

        // Pasta das chaves: DataProtection:KeysPath, ou o padrão histórico
        // {CertificateStorage:BasePath}/dp-keys. Vazio nos dois = pasta
        // padrão do próprio ASP.NET (Linux: ~/.aspnet/DataProtection-Keys)
        // — o Program.cs avisa no log onde as chaves estão.
        var keysPath = !string.IsNullOrWhiteSpace(protecao.KeysPath)
            ? protecao.KeysPath
            : !string.IsNullOrWhiteSpace(certOptions.BasePath) ? Path.Combine(certOptions.BasePath, "dp-keys") : string.Empty;

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            Directory.CreateDirectory(keysPath);
            dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        // Criptografia das chaves em disco:
        //  - certificado configurado (qualquer SO): chaves NOVAS saem
        //    criptografadas com ele. Chaves antigas gravadas sem
        //    criptografia continuam legíveis — o Data Protection lê as duas
        //    formas, então ligar isto não perde nenhum certificado já enviado.
        //    Quando a chave atual vencer (90 dias), a nova já nasce protegida.
        //  - sem certificado, no Windows: DPAPI de máquina (como antes).
        //  - sem certificado, fora do Windows: SEM criptografia (como antes) —
        //    o Program.cs registra um aviso na subida.
        string mecanismo;
        if (!string.IsNullOrWhiteSpace(protecao.CertificadoPfxPath))
        {
            // EphemeralKeySet fora do Windows: a chave privada fica só em
            // memória, sem cópia persistida em disco pelo runtime.
            var certificadoProtecao = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                protecao.CertificadoPfxPath, protecao.CertificadoSenha,
                OperatingSystem.IsWindows()
                    ? System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.DefaultKeySet
                    : System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);

            dataProtectionBuilder
                .ProtectKeysWithCertificate(certificadoProtecao)
                .UnprotectKeysWithAnyCertificate(certificadoProtecao);
            mecanismo = $"certificado {certificadoProtecao.Thumbprint}";
        }
        else if (OperatingSystem.IsWindows())
        {
            dataProtectionBuilder.ProtectKeysWithDpapi(protectToLocalMachine: true);
            mecanismo = "DPAPI (máquina)";
        }
        else
        {
            mecanismo = "nenhum";
        }

        services.AddSingleton(new ProtecaoChavesStatus(keysPath, mecanismo != "nenhum", mecanismo));

        services.AddScoped<CertificateFileStore>();
        services.AddScoped<ProtecaoChavesService>();
        services.AddScoped<IConsultarProtecaoChavesUseCase>(sp => sp.GetRequiredService<ProtecaoChavesService>());
        services.AddScoped<IReprotegerChavesUseCase>(sp => sp.GetRequiredService<ProtecaoChavesService>());
        services.AddScoped<ICertificateProvider, FileCertificateProvider>();
    }

    /// <summary>Raiz de disco pros documentos de Contrato — separada da raiz de certificados, sem criptografia (decisão do usuário).</summary>
    private static void AddDocumentStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentStorageOptions>(configuration.GetSection(DocumentStorageOptions.SectionName));
        services.AddScoped<ContratoDocumentoFileStore>();
    }
}
