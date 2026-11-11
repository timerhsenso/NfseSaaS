using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Infrastructure.Persistence;

namespace NfseSaaS.Infrastructure.Contratos;

/// <summary>Número do contrato no banco: próximo da empresa e unicidade (sem diferenciar maiúsculas).</summary>
public static class NumeracaoContrato
{
    public static async Task<string> ProximoAsync(AppDbContext db, Guid empresaId, int ano, CancellationToken cancellationToken)
    {
        var prefixo = $"{ano}";
        var existentes = await db.Contratos.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.Numero != null && c.Numero.Trim().StartsWith(prefixo))
            .Select(c => c.Numero)
            .ToListAsync(cancellationToken);
        return NumeroContratoRegras.Proximo(ano, existentes);
    }

    public static async Task GarantirUnicoAsync(AppDbContext db, Guid empresaId, string numero, Guid? ignorarContratoId, CancellationToken cancellationToken)
    {
        var maiusculo = numero.ToUpper();
        var repetido = await db.Contratos.AsNoTracking()
            .AnyAsync(c => c.EmpresaId == empresaId && c.Id != ignorarContratoId
                && c.Numero != null && c.Numero.ToUpper() == maiusculo, cancellationToken);
        if (repetido)
            throw new RegraNegocioException($"Já existe o contrato nº {numero} nesta empresa. Use outro número.");
    }
}
