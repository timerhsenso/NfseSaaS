namespace NfseSaaS.Infrastructure.DadosConta;

/// <summary>
/// Onde ficam os arquivos ZIP da exportação dos dados (D1). Vazio = pasta
/// temporária do sistema: o arquivo vale só 7 dias, não deve entrar no
/// backup e pode sumir num deploy (a tela então pede uma nova exportação).
/// </summary>
public sealed class ExportacaoDadosOptions
{
    public const string SectionName = "ExportacaoDados";

    public string BasePath { get; set; } = string.Empty;

    public string PastaBase() =>
        string.IsNullOrWhiteSpace(BasePath)
            ? Path.Combine(Path.GetTempPath(), "nfsesaas-exportacoes")
            : BasePath;

    /// <summary>{base}/{TenantId}/{ExportacaoId}.zip — uma pasta por conta.</summary>
    public string CaminhoArquivo(Guid tenantId, Guid exportacaoId) =>
        Path.Combine(PastaBase(), tenantId.ToString("N"), $"{exportacaoId:N}.zip");
}
