using Microsoft.EntityFrameworkCore;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Infrastructure.Persistence;
using Xunit;

namespace NfseSaaS.Tests;

/// <summary>
/// Campos com valor padrão no banco: o "sentinel" diz ao EF quando omitir o
/// valor no INSERT e deixar o banco decidir. Só monta o modelo (sem conexão).
/// </summary>
public class ValoresPadraoModeloTests
{
    private static AppDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=modelo_apenas")
                .Options,
            new TenantFixo());

    [Fact]
    public void Status_Rascunho_do_contrato_nao_e_trocado_pelo_default_do_banco()
    {
        using var db = CriarContexto();
        var status = db.Model.FindEntityType(typeof(Contrato))!.FindProperty(nameof(Contrato.Status))!;

        // Rascunho (0) é válido: o sentinel tem de ficar fora do enum.
        Assert.Equal((StatusContrato)(-1), status.Sentinel);
        Assert.False(Enum.IsDefined((StatusContrato)status.Sentinel!));
    }

    [Theory]
    [InlineData(typeof(Empresa), nameof(Empresa.TipoAmbiente))]
    [InlineData(typeof(Nfse), nameof(Nfse.TipoAmbiente))]
    [InlineData(typeof(MovimentacaoValorContrato), nameof(MovimentacaoValorContrato.Tipo))]
    [InlineData(typeof(MovimentacaoValorContrato), nameof(MovimentacaoValorContrato.Status))]
    public void Enum_sem_valor_zero_usa_o_default_do_banco_quando_nao_informado(Type entidade, string propriedade)
    {
        using var db = CriarContexto();
        var campo = db.Model.FindEntityType(entidade)!.FindProperty(propriedade)!;

        Assert.NotNull(campo.Sentinel);
        Assert.Equal(0, Convert.ToInt32(campo.Sentinel));
        Assert.False(Enum.IsDefined(campo.ClrType, campo.Sentinel!));
    }

    private sealed class TenantFixo : ICurrentTenant
    {
        public Guid? TenantId => Guid.Empty;

        public bool IsResolved => true;

        public void DefinirTenantIdDeSistema(Guid tenantId)
        {
        }
    }
}
