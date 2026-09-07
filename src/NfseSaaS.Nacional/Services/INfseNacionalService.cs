using NfseSaaS.Nacional.Models;
using NfseSaaS.Nacional.Responses;

namespace NfseSaaS.Nacional.Services;

/// <summary>Fachada do módulo: orquestra build → sign → gzip/base64 → envio → parse, para emitir e cancelar uma NFS-e.</summary>
public interface INfseNacionalService
{
    Task<NfseNacionalResponse> EmitirAsync(DpsRequest request, Guid empresaId, CancellationToken cancellationToken);

    /// <summary>Registra o evento de cancelamento (e101101) de uma NFS-e já autorizada.</summary>
    Task<EventoNacionalResponse> CancelarAsync(EventoCancelamentoRequest request, Guid empresaId, CancellationToken cancellationToken);
}
