namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Resultado do pedido de envio da nota ao tomador. O envio de verdade
/// acontece em segundo plano (fila de e-mails) — Enfileirado=true quer dizer
/// que entrou na fila; o resultado final aparece na tela de E-mails.
/// </summary>
public sealed record EnviarNfsePorEmailResult(bool Enfileirado, string? Destinatario, string? Motivo, IReadOnlyList<string>? Copias = null);

/// <summary>
/// Envia a nota AUTORIZADA (PDF e XML anexos) para o e-mail do tomador
/// (Cliente.Email), com as cópias do cadastro (Cliente.EmailsCopia, em Cc
/// ou Cco conforme Cliente.CopiaOculta).
/// - automatico=true (logo depois da autorização, em EmitirNfseUseCase):
///   só envia com Cliente.EnviarNfsePorEmail ligado e, sem e-mail
///   cadastrado, só não envia — nunca falha a emissão por causa do e-mail.
/// - automatico=false (botão "Enviar por e-mail" da tela): envia mesmo com
///   o envio automático desligado no cliente; sem e-mail cadastrado,
///   RegraNegocioException com a orientação.
/// O texto vem do modelo do cliente, da empresa ou o padrão da conta
/// (cadastro Modelos de e-mail); <paramref name="texto"/> troca o modelo,
/// o assunto e a mensagem só neste envio.
/// </summary>
public interface IEnviarNfsePorEmailUseCase
{
    Task<EnviarNfsePorEmailResult> ExecutarAsync(Guid nfseId, bool automatico, TextoEmailNota? texto, CancellationToken cancellationToken);
}

/// <summary>
/// Modal de envio, só para este envio: o modelo escolhido (respostas,
/// cópia oculta e rodapé vêm dele) e o assunto e a mensagem como ficaram.
/// Assunto/mensagem vazios = os do modelo.
/// </summary>
public sealed record TextoEmailNota(string? Assunto, string? Mensagem, Guid? ModeloId = null);
