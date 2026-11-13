using Microsoft.Extensions.Options;

namespace NfseSaaS.Infrastructure.Documents;

/// <summary>
/// DOC1: lê/grava/apaga em disco os PDFs de documento da Empresa, na mesma
/// raiz dos documentos de contrato (DocumentStorage:BasePath), sem
/// criptografia (mesma decisão dos contratos):
/// {BasePath}/{TenantId}/{EmpresaId}/documentos/{DocumentoId}.pdf.
/// A exclusão e o encerramento da conta já apagam {BasePath}/{TenantId}
/// inteiro (ContratoDocumentoFileStore.ExcluirPastaConta).
/// </summary>
public sealed class DocumentoEmpresaFileStore
{
    private const string Extensao = ".pdf";

    private readonly DocumentStorageOptions _options;

    public DocumentoEmpresaFileStore(IOptions<DocumentStorageOptions> options)
    {
        _options = options.Value;
    }

    private string PastaEmpresa(Guid tenantId, Guid empresaId) =>
        Path.Combine(_options.BasePath, tenantId.ToString("N"), empresaId.ToString("N"), "documentos");

    private string Caminho(Guid tenantId, Guid empresaId, Guid documentoId) =>
        Path.Combine(PastaEmpresa(tenantId, empresaId), $"{documentoId:N}{Extensao}");

    public async Task SalvarAsync(Guid tenantId, Guid empresaId, Guid documentoId, byte[] conteudo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new InvalidOperationException("DocumentStorage:BasePath não configurado.");

        Directory.CreateDirectory(PastaEmpresa(tenantId, empresaId));
        await File.WriteAllBytesAsync(Caminho(tenantId, empresaId, documentoId), conteudo, cancellationToken);
    }

    public async Task<byte[]> LerAsync(Guid tenantId, Guid empresaId, Guid documentoId, CancellationToken cancellationToken)
    {
        var caminho = Caminho(tenantId, empresaId, documentoId);
        if (!File.Exists(caminho))
            throw new FileNotFoundException("Arquivo do documento não encontrado no servidor.");

        return await File.ReadAllBytesAsync(caminho, cancellationToken);
    }

    public void Excluir(Guid tenantId, Guid empresaId, Guid documentoId)
    {
        var caminho = Caminho(tenantId, empresaId, documentoId);
        if (File.Exists(caminho))
            File.Delete(caminho);
    }

    /// <summary>Exclusão da Empresa: some a pasta de documentos dela.</summary>
    public void ExcluirPastaEmpresa(Guid tenantId, Guid empresaId)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            return;
        var pasta = PastaEmpresa(tenantId, empresaId);
        if (Directory.Exists(pasta))
            Directory.Delete(pasta, recursive: true);
    }
}
