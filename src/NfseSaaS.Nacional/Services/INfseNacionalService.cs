using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Responses;

namespace NfseSaaS.Nacional.Services;

/// <summary>Fachada do módulo: orquestra build → sign → gzip/base64 → envio → parse, para emitir uma NFS-e.</summary>
public interface INfseNacionalService
{
    Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken);
}
