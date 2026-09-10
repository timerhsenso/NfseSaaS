using Microsoft.Extensions.Options;

namespace NfseSaaS.Infrastructure.Documents;

/// <summary>
/// Lê/grava/apaga em disco os documentos anexados a um Contrato. SEM
/// criptografia (decisão do usuário — diferente do certificado .pfx),
/// então a segurança aqui é só permissão de pasta restrita no NTFS
/// (mesma prática já recomendada pra CertificateStorage:BasePath).
///
/// Estrutura: {BasePath}/{TenantId}/{EmpresaId}/contratos/{ContratoId}/{DocumentoId}.{extensao}
/// — isolamento físico por Tenant/Empresa, não só filtro de banco (ver
/// discussão no chat: "não deve misturar tenant").
/// </summary>
public sealed class ContratoDocumentoFileStore
{
    private readonly DocumentStorageOptions _options;

    public ContratoDocumentoFileStore(IOptions<DocumentStorageOptions> options)
    {
        _options = options.Value;
    }

    private string PastaContrato(Guid tenantId, Guid empresaId, Guid contratoId) =>
        Path.Combine(_options.BasePath, tenantId.ToString("N"), empresaId.ToString("N"), "contratos", contratoId.ToString("N"));

    private string CaminhoArquivo(Guid tenantId, Guid empresaId, Guid contratoId, Guid documentoId, string extensao) =>
        Path.Combine(PastaContrato(tenantId, empresaId, contratoId), $"{documentoId:N}{extensao}");

    public async Task SalvarAsync(Guid tenantId, Guid empresaId, Guid contratoId, Guid documentoId, string extensao, byte[] conteudo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BasePath))
            throw new InvalidOperationException("DocumentStorage:BasePath não configurado.");

        var pasta = PastaContrato(tenantId, empresaId, contratoId);
        Directory.CreateDirectory(pasta);

        await File.WriteAllBytesAsync(CaminhoArquivo(tenantId, empresaId, contratoId, documentoId, extensao), conteudo, cancellationToken);
    }

    public async Task<byte[]> LerAsync(Guid tenantId, Guid empresaId, Guid contratoId, Guid documentoId, string extensao, CancellationToken cancellationToken)
    {
        var caminho = CaminhoArquivo(tenantId, empresaId, contratoId, documentoId, extensao);
        if (!File.Exists(caminho))
            throw new FileNotFoundException($"Documento não encontrado em '{caminho}'.");

        return await File.ReadAllBytesAsync(caminho, cancellationToken);
    }

    public void Excluir(Guid tenantId, Guid empresaId, Guid contratoId, Guid documentoId, string extensao)
    {
        var caminho = CaminhoArquivo(tenantId, empresaId, contratoId, documentoId, extensao);
        if (File.Exists(caminho))
            File.Delete(caminho);
    }

    /// <summary>Chamado por ExcluirContratoUseCase — remove a pasta inteira do Contrato (todos os documentos dele) do disco.</summary>
    public void ExcluirPastaContrato(Guid tenantId, Guid empresaId, Guid contratoId)
    {
        var pasta = PastaContrato(tenantId, empresaId, contratoId);
        if (Directory.Exists(pasta))
            Directory.Delete(pasta, recursive: true);
    }
}
