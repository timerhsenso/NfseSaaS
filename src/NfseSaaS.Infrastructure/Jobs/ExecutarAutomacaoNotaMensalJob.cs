using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.AutomacaoNotaMensal;
using NfseSaaS.Application.UseCases.NotaMensal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Email;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Jobs;

/// <summary>
/// Corpo real da automação — chamado pelo Hangfire uma vez por Empresa
/// (ver AutomacaoNotaMensalJobHelper/AtualizarConfiguracaoAutomacaoNotaMensalUseCase).
/// Cria o próprio escopo de DI (IServiceScopeFactory), igual
/// EmailDispatchHostedService: roda fora do ciclo de uma requisição
/// HTTP, então nada aqui pode contar com um AppDbContext/ICurrentTenant
/// já resolvidos por fora.
///
/// Modo ListarParaRevisao ainda não está implementado (fase seguinte da
/// automação) — por enquanto só loga e sai, sem emitir nem avisar nada.
/// </summary>
public sealed class ExecutarAutomacaoNotaMensalJob : IExecutarAutomacaoNotaMensalJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExecutarAutomacaoNotaMensalJob> _logger;
    private readonly EmailOptions _emailOptions;

    public ExecutarAutomacaoNotaMensalJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ExecutarAutomacaoNotaMensalJob> logger,
        IOptions<EmailOptions> emailOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _emailOptions = emailOptions.Value;
    }

    public async Task ExecutarAsync(Guid empresaId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // .IgnoreQueryFilters(): ainda não há Tenant resolvido neste
        // ponto — é justamente esta consulta que vai descobrir de qual
        // Tenant se trata, pra resolver logo em seguida.
        var empresa = await db.Empresas.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == empresaId);

        if (empresa is null)
        {
            // Empresa excluída depois do job ter sido agendado, mas
            // antes de disparar — não é erro do job, só desatualização.
            // Não remove o RecurringJob sozinho aqui (evita corrida com
            // uma exclusão em andamento); a próxima sincronização de
            // startup ou edição da configuração resolve.
            _logger.LogWarning(
                "Automação de Nota Mensal: Empresa {EmpresaId} não encontrada — pulando execução.", empresaId);
            return;
        }

        // A PARTIR DAQUI, todo _db.<Algo> desta execução já enxerga o
        // Tenant certo — leitura (Global Query Filter) e escrita
        // (AppDbContext.ApplyTenantIsolation) consultam a MESMA
        // instância de ICurrentTenant injetada no AppDbContext deste
        // escopo. Ver ICurrentTenant.DefinirTenantIdDeSistema.
        var currentTenant = scope.ServiceProvider.GetRequiredService<ICurrentTenant>();
        currentTenant.DefinirTenantIdDeSistema(empresa.TenantId);

        var configuracao = await db.ConfiguracoesAutomacaoNotaMensal
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId);

        if (configuracao is null || !configuracao.Ativo)
        {
            // Config foi desativada/apagada entre o agendamento e o
            // disparo — o RecurringJob correspondente será removido na
            // próxima vez que a tela salvar ou no próximo ciclo de
            // sincronização de startup.
            _logger.LogInformation(
                "Automação de Nota Mensal: Empresa {EmpresaId} sem configuração ativa — pulando execução.", empresaId);
            return;
        }

        // Mês atual, em horário de Brasília — nunca DateTime.Now puro
        // (servidor pode rodar em UTC; perto da virada de mês isso
        // erraria o mês certo por algumas horas, mesma classe do bug
        // antigo do dhEmi).
        var agoraBrasilia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AutomacaoNotaMensalJobHelper.FusoBrasilia);
        var competencia = new DateOnly(agoraBrasilia.Year, agoraBrasilia.Month, 1);

        if (configuracao.Modo == ModoAutomacaoNotaMensal.Automatico)
        {
            await ExecutarModoAutomaticoAsync(scope.ServiceProvider, empresa, competencia);
        }
        else
        {
            await ExecutarModoListarParaRevisaoAsync(scope.ServiceProvider, empresa, competencia);
        }
    }

    private async Task ExecutarModoAutomaticoAsync(IServiceProvider servicos, Empresa empresa, DateOnly competencia)
    {
        var listarCandidatos = servicos.GetRequiredService<IListarCandidatosNotaMensalUseCase>();
        var emitirLote = servicos.GetRequiredService<IEmitirNotaMensalLoteUseCase>();
        var emailQueue = servicos.GetRequiredService<IEmailQueueService>();

        var candidatos = await listarCandidatos.ExecutarAsync(empresa.Id, competencia, CancellationToken.None);

        // Contratos já emitidos nesta competência (manual, ou uma
        // execução anterior deste mesmo job) não precisam passar pelo
        // lote de novo. A idempotência de verdade já está garantida
        // dentro de EmitirNotaMensalLoteUseCase (Id determinístico) —
        // filtrar aqui só evita uma chamada desnecessária à SEFIN pra
        // algo que já sabemos que não vai gerar nota nova.
        var pendentes = candidatos.Where(c => !c.JaEmitidoNestaCompetencia).ToList();

        // Log com as contagens (não só "nenhum pendente") — sem isso,
        // "0 candidatos" (join com Contrato/Cliente não achou nada
        // elegível) e "N candidatos, mas todos já emitidos" ficam
        // indistinguíveis no log, obrigando a caçar na mão via SQL toda
        // vez que uma Empresa não emite nada numa competência.
        _logger.LogInformation(
            "Automação de Nota Mensal: Empresa {EmpresaId}, competência {Competencia:yyyy-MM} — {TotalCandidatos} candidato(s) elegíve(is) no total, {JaEmitidos} já emitido(s), {Pendentes} pendente(s).",
            empresa.Id, competencia, candidatos.Count, candidatos.Count - pendentes.Count, pendentes.Count);

        if (pendentes.Count == 0)
            return;

        var request = new EmitirNotaMensalLoteRequest(
            empresa.Id,
            competencia,
            pendentes.Select(c => new EmitirNotaMensalItemRequest(c.ContratoId, ValorTotalAjustado: null)).ToList());

        var resultados = await emitirLote.ExecutarAsync(request, CancellationToken.None);

        var sucessos = resultados.Where(r => r.Sucesso).ToList();
        var falhas = resultados.Where(r => !r.Sucesso).ToList();

        _logger.LogInformation(
            "Automação de Nota Mensal: Empresa {EmpresaId}, competência {Competencia:yyyy-MM} — {Sucessos} emitida(s), {Falhas} falharam.",
            empresa.Id, competencia, sucessos.Count, falhas.Count);

        await EnviarEmailResumoAsync(emailQueue, empresa, competencia, sucessos, falhas);
    }

    private static async Task EnviarEmailResumoAsync(
        IEmailQueueService emailQueue,
        Empresa empresa,
        DateOnly competencia,
        IReadOnlyList<ItemResultadoNotaMensalResponse> sucessos,
        IReadOnlyList<ItemResultadoNotaMensalResponse> falhas)
    {
        var nomeCompetencia = competencia.ToString("MM/yyyy");
        var assunto = falhas.Count == 0
            ? $"Nota Mensal {nomeCompetencia} emitida — {empresa.RazaoSocial}"
            : $"Nota Mensal {nomeCompetencia} — {sucessos.Count} emitida(s), {falhas.Count} com falha — {empresa.RazaoSocial}";

        var corpo = new StringBuilder();
        corpo.Append($"<p>A automação de Nota Mensal da competência <strong>{nomeCompetencia}</strong> rodou para <strong>{WebUtility.HtmlEncode(empresa.RazaoSocial)}</strong>.</p>");
        corpo.Append($"<p><strong>{sucessos.Count}</strong> nota(s) emitida(s) com sucesso.</p>");

        if (falhas.Count > 0)
        {
            corpo.Append($"<p><strong>{falhas.Count}</strong> contrato(s) com falha:</p><ul>");
            foreach (var falha in falhas)
            {
                var motivo = WebUtility.HtmlEncode(falha.Mensagem ?? "motivo não informado");
                corpo.Append($"<li>{WebUtility.HtmlEncode(falha.ClienteNome)} — {WebUtility.HtmlEncode(falha.DescricaoNota)}: {motivo}</li>");
            }
            corpo.Append("</ul><p>Esses contratos podem ser emitidos manualmente pela tela de Nota Mensal.</p>");
        }

        // usuarioId: null — não foi um clique de usuário que disparou
        // isto, foi o sistema (mesmo raciocínio de UserId=null no
        // AuditLog nesta execução). destinatario: o e-mail de contato
        // já cadastrado na própria Empresa (campo obrigatório no
        // cadastro) — sem precisar resolver "qual usuário Administrador
        // recebe isto".
        await emailQueue.EnfileirarAsync(
            tipo: "AutomacaoNotaMensalResumo",
            usuarioId: null,
            destinatario: empresa.Email,
            assunto: assunto,
            corpoHtml: corpo.ToString(),
            cancellationToken: CancellationToken.None);
    }

    private async Task ExecutarModoListarParaRevisaoAsync(IServiceProvider servicos, Empresa empresa, DateOnly competencia)
    {
        var listarCandidatos = servicos.GetRequiredService<IListarCandidatosNotaMensalUseCase>();
        var emailQueue = servicos.GetRequiredService<IEmailQueueService>();

        var candidatos = await listarCandidatos.ExecutarAsync(empresa.Id, competencia, CancellationToken.None);
        var pendentes = candidatos.Where(c => !c.JaEmitidoNestaCompetencia).ToList();

        _logger.LogInformation(
            "Automação de Nota Mensal (revisão): Empresa {EmpresaId}, competência {Competencia:yyyy-MM} — {TotalCandidatos} candidato(s) elegíve(is) no total, {Pendentes} pendente(s) pra revisar.",
            empresa.Id, competencia, candidatos.Count, pendentes.Count);

        // Nada pendente — não manda e-mail de "vazio", ninguém precisa
        // ser avisado que não há nada pra revisar.
        if (pendentes.Count == 0)
            return;

        await EnviarEmailRevisaoAsync(emailQueue, empresa, competencia, pendentes);
    }

    private async Task EnviarEmailRevisaoAsync(
        IEmailQueueService emailQueue,
        Empresa empresa,
        DateOnly competencia,
        IReadOnlyList<ContratoCandidatoNotaMensalResponse> pendentes)
    {
        var nomeCompetencia = competencia.ToString("MM/yyyy");
        var assunto = $"Nota Mensal {nomeCompetencia} pronta para revisão — {pendentes.Count} contrato(s) — {empresa.RazaoSocial}";

        var corpo = new StringBuilder();
        corpo.Append($"<p>A competência <strong>{nomeCompetencia}</strong> de <strong>{WebUtility.HtmlEncode(empresa.RazaoSocial)}</strong> está pronta para revisão — <strong>{pendentes.Count}</strong> contrato(s) elegível(is), nenhum emitido ainda (modo automático desligado para esta Empresa).</p>");
        corpo.Append("<ul>");
        foreach (var candidato in pendentes)
        {
            corpo.Append($"<li>{WebUtility.HtmlEncode(candidato.ClienteNome)} — {WebUtility.HtmlEncode(candidato.Descricao)}: {candidato.ValorTotal:C}</li>");
        }
        corpo.Append("</ul>");

        // AppBaseUrl vazio (padrão hoje — ver EmailOptions) = e-mail sai
        // sem link clicável, só com a orientação textual. Nada quebra,
        // só fica menos conveniente até a URL de produção ser configurada.
        if (string.IsNullOrWhiteSpace(_emailOptions.AppBaseUrl))
        {
            corpo.Append("<p>Abra a tela de Nota Mensal no sistema e selecione a competência acima para revisar e confirmar a emissão.</p>");
        }
        else
        {
            var link = $"{_emailOptions.AppBaseUrl.TrimEnd('/')}/Nfse?empresaId={empresa.Id}&competencia={competencia:yyyy-MM}&abrirNotaMensal=1";
            corpo.Append($"<p><a href=\"{link}\">Clique aqui para revisar e confirmar a emissão</a></p>");
        }

        await emailQueue.EnfileirarAsync(
            tipo: "AutomacaoNotaMensalRevisao",
            usuarioId: null,
            destinatario: empresa.Email,
            assunto: assunto,
            corpoHtml: corpo.ToString(),
            cancellationToken: CancellationToken.None);
    }
}
