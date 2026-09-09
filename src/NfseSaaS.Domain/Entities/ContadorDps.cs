namespace NfseSaaS.Domain.Entities;

/// <summary>
/// Contador do último NumeroDps emitido por Empresa/Série. Tabela técnica
/// de suporte — nunca exposta via API, sem Id/CreatedAt (não é
/// BaseEntity) e sem ITenantEntity (não sofre nem precisa do Global Query
/// Filter: EmpresaId já é suficiente, e o acesso é sempre via SQL bruto
/// no próprio caso de uso de emissão, nunca via LINQ genérico).
///
/// Existe para resolver, de forma atômica no Postgres (UPSERT com
/// ON CONFLICT ... DO UPDATE ... RETURNING), a corrida que existia no
/// cálculo antigo em C# (MAX(NumeroDps) + 1): duas emissões simultâneas
/// para a mesma Empresa podiam ler o mesmo "último número" antes de
/// qualquer uma delas gravar, gerando DPS com número duplicado.
/// </summary>
public sealed class ContadorDps
{
    public Guid EmpresaId { get; set; }

    public string SerieDps { get; set; } = string.Empty;

    public int UltimoNumero { get; set; }
}
