using QuestPDF.Fluent;

namespace NfseSaaS.Infrastructure.Danfse;

/// <summary>
/// Gera o DANFSe (PDF) localmente a partir do XML da NFS-e já autorizada
/// — substitui a antiga API de geração de PDF da SEFIN Nacional,
/// descontinuada em 03/08/2026 (NT 008/2026). Ver DanfseDocument pro
/// layout e suas limitações documentadas.
/// </summary>
public static class DanfsePdfGenerator
{
    public static byte[] Gerar(string xmlNfse, bool cancelada, string? caminhoLogo = null)
    {
        var dados = new DanfseXmlDados(xmlNfse);
        var documento = new DanfseDocument(dados, cancelada, caminhoLogo);
        return documento.GeneratePdf();
    }
}
