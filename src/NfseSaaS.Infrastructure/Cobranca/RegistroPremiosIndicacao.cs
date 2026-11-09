using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Cobranca;

/// <summary>
/// Registro dos prêmios do "Indique e ganhe" no PRIMEIRO pagamento de uma
/// conta indicada: um prêmio para ela e um para quem indicou. Usado pelo
/// webhook e pela conciliação diária (quando o webhook do primeiro pagamento
/// se perdeu). Só ADICIONA no contexto: quem chama grava junto com a sua
/// alteração (mesma transação). O índice único (IndicadoTenantId, Papel)
/// segura a repetição; a consulta antes evita depender do erro de gravação.
/// </summary>
internal static class RegistroPremiosIndicacao
{
    /// <summary>Registra os dois prêmios, se couber. Devolve true quando registrou.</summary>
    public static async Task<bool> RegistrarAsync(AppDbContext db, Guid tenantId, string? pagamentoId, CancellationToken cancellationToken)
    {
        var indicadoPor = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.IndicadoPorTenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!RegrasPremioIndicacao.GeraPremio(primeiroPagamento: true, tenantId, indicadoPor))
            return false;

        if (await db.PremiosIndicacao.AsNoTracking().AnyAsync(p => p.IndicadoTenantId == tenantId, cancellationToken))
            return false;

        var origem = pagamentoId is null ? null : pagamentoId.Length <= 60 ? pagamentoId : pagamentoId[..60];
        db.PremiosIndicacao.Add(new PremioIndicacao
        {
            TenantId = tenantId, IndicadoTenantId = tenantId, Papel = PapelPremioIndicacao.ContaIndicada,
            AsaasPagamentoOrigemId = origem, Detalhe = "Aguardando aplicação."
        });
        db.PremiosIndicacao.Add(new PremioIndicacao
        {
            TenantId = indicadoPor!.Value, IndicadoTenantId = tenantId, Papel = PapelPremioIndicacao.QuemIndicou,
            AsaasPagamentoOrigemId = origem, Detalhe = "Aguardando aplicação."
        });
        return true;
    }

    /// <summary>A conta tem prêmio guardado esperando um pagamento dela (quem INDICOU)?</summary>
    public static Task<bool> TemPendentesAsync(AppDbContext db, Guid tenantId, CancellationToken cancellationToken) =>
        db.PremiosIndicacao.AsNoTracking().AnyAsync(
            p => p.TenantId == tenantId && p.Status == StatusPremioIndicacao.Pendente, cancellationToken);
}
