using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.Nfse;

/// <summary>
/// Modal "Enviar nota por e-mail": para quem vai, com o texto já preenchido
/// pelo modelo (o usuário pode ajustar só para este envio) e o último envio.
/// </summary>
/// <param name="Impedimento">Por que não dá para enviar (não autorizada, sem e-mail); nulo = pode.</param>
/// <param name="ModeloId">Modelo usado no texto (nulo = texto padrão do sistema); Modelos = as opções para trocar.</param>
public sealed record PreviaEmailNfseResponse(
    Guid NfseId,
    Guid? ModeloId,
    IReadOnlyList<NfseSaaS.Application.UseCases.EmailNota.OpcaoModeloEmailNota> Modelos,
    string? Destinatario,
    IReadOnlyList<string> Copias,
    bool CopiasOcultas,
    string? CopiaOcultaPara,
    string? ResponderPara,
    string Assunto,
    string Mensagem,
    bool Homologacao,
    DateTimeOffset? UltimoEnvioEm,
    StatusEmailLog? UltimoStatus,
    string? Impedimento,
    IReadOnlyList<NfseSaaS.Application.UseCases.DocumentosEmpresa.DocumentoEmailPrevia>? Documentos = null);

/// <param name="PularJaEnviadas">Não reenvia nota que já tem e-mail enviado ou na fila.</param>
/// <param name="EnviarDocumentosVencidos">DOC2: documento exigido vencido vai assim mesmo (padrão: não vai; o alerta fica registrado).</param>
public sealed record EnvioEmailLoteRequest(IReadOnlyList<Guid> NfseIds, bool PularJaEnviadas = true, bool EnviarDocumentosVencidos = false);

/// <summary>Resumo antes de enviar várias notas.</summary>
/// <param name="Enviaveis">Autorizadas e com e-mail do cliente (inclui as já enviadas).</param>
/// <param name="Clientes">Clientes diferentes entre as enviáveis.</param>
/// <param name="ComDocumentoVencido">DOC2: enviáveis com algum documento exigido vencido.</param>
/// <param name="ComDocumentoFaltando">DOC2: enviáveis com algum documento exigido que a empresa não enviou.</param>
public sealed record PreviaEnvioEmailLoteResponse(
    int Total, int Enviaveis, int JaEnviadas, int SemEmail, int NaoAutorizadas, int Clientes,
    int ComDocumentoVencido = 0, int ComDocumentoFaltando = 0);

public sealed record NotaNaoEnviadaPorEmail(Guid NfseId, string Numero, string Cliente, string Motivo);

public sealed record ResultadoEnvioEmailLoteResponse(int Enfileiradas, int Puladas, IReadOnlyList<NotaNaoEnviadaPorEmail> NaoEnviadas);

public interface IEnvioEmailNfseUseCase
{
    public const int MaximoPorLote = 200;

    /// <summary><paramref name="modeloId"/>: o modelo escolhido no modal (nulo = o que vale para a nota).</summary>
    Task<PreviaEmailNfseResponse> PreviaAsync(Guid nfseId, Guid? modeloId, CancellationToken cancellationToken);

    Task<PreviaEnvioEmailLoteResponse> PreviaLoteAsync(EnvioEmailLoteRequest request, CancellationToken cancellationToken);

    /// <summary>Um e-mail por nota, cada um com o modelo da sua empresa.</summary>
    Task<ResultadoEnvioEmailLoteResponse> EnviarLoteAsync(EnvioEmailLoteRequest request, CancellationToken cancellationToken);
}
