using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.UseCases;

/// <summary>
/// Retenção dos convites do "Indique e ganhe" (B7, Política 1.1, item 7):
/// passado o prazo de <see cref="RegrasConviteIndicacao.RetencaoMeses"/>
/// meses, o convite e o e-mail de convite registrado em "E-mails enviados"
/// são apagados — os dois guardam o endereço de quem foi convidado.
///
/// Apaga também o convite de quem chegou a criar conta: depois do prazo ele
/// não serve para mais nada (a ligação "quem indicou" fica na própria conta,
/// em Tenant.IndicadoPorTenantId, e os prêmios não dependem do convite).
/// As regras anti-spam olham só os últimos 30 dias, então nada muda nelas.
///
/// Todas as contas de uma vez (job sem conta atual): IgnoreQueryFilters.
/// </summary>
public sealed class LimparConvitesIndicacaoService : ILimparConvitesIndicacaoUseCase
{
    private readonly AppDbContext _db;
    private readonly ILogger<LimparConvitesIndicacaoService> _logger;

    public LimparConvitesIndicacaoService(AppDbContext db, ILogger<LimparConvitesIndicacaoService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ResultadoLimpezaConvites> ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var limite = RegrasConviteIndicacao.LimiteRetencao(agora);

        var convites = await _db.ConvitesIndicacao.IgnoreQueryFilters()
            .Where(c => c.CreatedAt < limite)
            .ExecuteDeleteAsync(cancellationToken);

        // O e-mail de convite fica pelo mesmo prazo (destinatário = convidado).
        // Pendente velho não existe na prática (a fila tenta por poucos dias), mas
        // também sai: depois do prazo não pode mais ser enviado.
        var emails = await _db.EmailLogs.IgnoreQueryFilters()
            .Where(e => e.Tipo == TipoEmailCatalogo.ConviteIndicacao && e.CreatedAt < limite)
            .ExecuteDeleteAsync(cancellationToken);

        if (convites > 0 || emails > 0)
            _logger.LogInformation(
                "Retenção dos convites de indicação: {Convites} convite(s) e {Emails} e-mail(s) de convite anteriores a {Limite:dd/MM/yyyy} apagados.",
                convites, emails, limite);

        return new ResultadoLimpezaConvites(convites, emails);
    }
}
