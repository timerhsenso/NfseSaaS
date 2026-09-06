using System.IO.Compression;
using System.Text;

namespace NfseSaaS.Nacional.Helpers;

/// <summary>
/// Compactação GZip+Base64 exigida pelo protocolo da SEFIN Nacional para o
/// XML da DPS (envio) e o XML da NFS-e (retorno). Utilitário puro, sem
/// regra fiscal — portado diretamente da POC validada (GZipBase64/GunzipBase64).
/// </summary>
public static class GZipHelper
{
    public static string ComprimirParaBase64(string texto)
    {
        var bytes = Encoding.UTF8.GetBytes(texto);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return Convert.ToBase64String(output.ToArray());
    }

    public static string DescomprimirDeBase64(string base64)
    {
        var compressed = Convert.FromBase64String(base64);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
