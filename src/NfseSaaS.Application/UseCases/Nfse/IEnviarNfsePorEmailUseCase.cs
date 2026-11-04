namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Resultado do pedido de envio da nota ao tomador. O envio de verdade
/// acontece em segundo plano (fila de e-mails) — Enfileirado=true quer dizer
/// que entrou na fila; o resultado final aparece na tela de E-mails.
/// </summary>
public sealed record EnviarNfsePorEmailResult(bool Enfileirado, string? Destinatario, string? Motivo);

/// <summary>
/// Envia a nota AUTORIZADA (PDF e XML anexos) para o e-mail do tomador
/// (Cliente.Email).
/// - automatico=true (logo depois da autorização, em EmitirNfseUseCase):
///   respeita Cliente.NaoEnviarNfsePorEmail e, sem e-mail cadastrado, só
///   não envia — nunca falha a emissão por causa do e-mail.
/// - automatico=false (botão "Enviar por e-mail" da tela): envia mesmo com
///   o envio automático desligado no cliente; sem e-mail cadastrado,
///   RegraNegocioException com a orientação.
/// </summary>
public interface IEnviarNfsePorEmailUseCase
{
    Task<EnviarNfsePorEmailResult> ExecutarAsync(Guid nfseId, bool automatico, CancellationToken cancellationToken);
}
