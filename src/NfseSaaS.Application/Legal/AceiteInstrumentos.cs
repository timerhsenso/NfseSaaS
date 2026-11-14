using NfseSaaS.Domain.Legal;

namespace NfseSaaS.Application.Legal;

/// <summary>Empresa que ainda não tem a autorização (A1 ou emissão automática) na versão atual.</summary>
public sealed record EmpresaPendenteAutorizacao(Guid EmpresaId, string Nome, string Cnpj);

/// <summary>
/// O que falta o usuário aceitar além dos Termos e da Política (JUR1).
/// DPA e autorizações são do administrador; para os outros usuários as listas vêm vazias.
/// </summary>
/// <param name="DpaPendente">A conta ainda não aceitou o DPA da versão publicada.</param>
/// <param name="DpaObrigatorio">O DPA pendente já vale (sem ele o administrador não usa o sistema).</param>
public sealed record PendenciasInstrumentos(
    bool EhAdministrador,
    string? VersaoDpa,
    bool DpaPendente,
    bool DpaObrigatorio,
    string? VersaoAutorizacaoCertificado,
    IReadOnlyList<EmpresaPendenteAutorizacao> Certificados,
    string? VersaoAutorizacaoEmissaoAutomatica,
    IReadOnlyList<EmpresaPendenteAutorizacao> EmissaoAutomatica)
{
    public static readonly PendenciasInstrumentos Nenhuma = new(false, null, false, false, null, [], null, []);

    public bool AlgumaPendente => DpaPendente || Certificados.Count > 0 || EmissaoAutomatica.Count > 0;
}

/// <summary>Um documento aceito, para a lista "Documentos aceitos" e o comprovante.</summary>
/// <param name="Origem">"termos" (Termos e Política, por usuário) ou "instrumento".</param>
public sealed record DocumentoAceitoResponse(
    Guid Id,
    string Origem,
    TipoDocumentoLegal Tipo,
    string Nome,
    string Versao,
    string Url,
    string UsuarioEmail,
    string? Empresa,
    string? CnpjEmpresa,
    DateTimeOffset AceitoEm,
    string? Ip,
    string? HashTexto,
    string? CertificadoImpressaoDigital,
    DateTimeOffset? RevogadoEm,
    string? MotivoRevogacao);

/// <summary>Arquivo pronto para baixar.</summary>
public sealed record ArquivoGerado(string NomeArquivo, string TipoConteudo, byte[] Conteudo);

/// <summary>
/// JUR1: aceite do DPA e das autorizações (A1, emissão automática), com o
/// hash do texto aceito, e a lista dos documentos aceitos da conta. Os
/// métodos Registrar* e Revogar* NÃO salvam (quem chama dá SaveChanges,
/// junto do que motivou o aceite).
/// </summary>
public interface IAceiteInstrumentosService
{
    Task<PendenciasInstrumentos> PendenciasAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>O DPA vigente falta e este usuário é administrador (o filtro de aceite manda para a tela).</summary>
    Task<bool> DpaObrigatorioPendenteAsync(Guid usuarioId, CancellationToken cancellationToken);

    Task RegistrarDpaAsync(Guid usuarioId, string origem, CancellationToken cancellationToken);

    Task RegistrarAutorizacaoCertificadoAsync(Guid usuarioId, Guid empresaId, string origem, CancellationToken cancellationToken);

    Task RegistrarAutorizacaoEmissaoAutomaticaAsync(Guid usuarioId, Guid empresaId, string origem, CancellationToken cancellationToken);

    /// <summary>Revoga a autorização vigente da empresa (se houver). Devolve se revogou alguma.</summary>
    Task<bool> RevogarAsync(TipoDocumentoLegal tipo, Guid empresaId, Guid usuarioId, string motivo, CancellationToken cancellationToken);

    /// <summary>A empresa tem a autorização deste tipo, na versão atual, não revogada?</summary>
    Task<bool> AutorizacaoVigenteAsync(TipoDocumentoLegal tipo, Guid empresaId, CancellationToken cancellationToken);

    /// <summary>
    /// Documentos aceitos na conta (Termos e Política de cada usuário, DPA e
    /// autorizações), mais novos primeiro. O administrador vê os da conta
    /// toda; os outros usuários, só os próprios.
    /// </summary>
    Task<IReadOnlyList<DocumentoAceitoResponse>> ListarAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>Comprovante em PDF de um aceite que o usuário pode ver (registro + texto do documento quando é instrumento). Nulo = não encontrado.</summary>
    Task<ArquivoGerado?> ComprovanteAsync(Guid usuarioId, string origem, Guid id, CancellationToken cancellationToken);
}
