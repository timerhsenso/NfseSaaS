using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>Uma versão (um arquivo enviado) de um documento.</summary>
public sealed record VersaoDocumentoEmpresaResponse(
    Guid Id,
    string NomeArquivo,
    long TamanhoBytes,
    DateOnly? DataEmissao,
    DateOnly? ValidaAte,
    SituacaoDocumentoEmpresa Situacao,
    string SituacaoTexto,
    int? DiasParaVencer,
    string? Observacao,
    DateTimeOffset EnviadoEm,
    string? EnviadoPor);

/// <summary>Uma linha da tela: o "lugar" do documento e a versão que vale (nula = ainda não enviado).</summary>
public sealed record ItemDocumentoEmpresaResponse(
    TipoDocumentoEmpresa Tipo,
    string Titulo,
    string? NomePersonalizado,
    string Orgao,
    string? LinkEmissao,
    VersaoDocumentoEmpresaResponse? Atual,
    int TotalVersoes);

/// <param name="EnvioLiberado">PL1 (recurso "documentos-empresa"): false = só ver e baixar o que já existe.</param>
public sealed record ListaDocumentosEmpresaResponse(
    Guid EmpresaId,
    bool EnvioLiberado,
    string? MensagemBloqueio,
    IReadOnlyList<TipoDocumentoInfo> Tipos,
    IReadOnlyList<ItemDocumentoEmpresaResponse> Itens);

public sealed record EnviarDocumentoEmpresaRequest(
    Guid EmpresaId,
    TipoDocumentoEmpresa Tipo,
    string? NomePersonalizado,
    DateOnly? DataEmissao,
    DateOnly? ValidaAte,
    string? Observacao);

public sealed record ArquivoDocumentoEmpresa(string NomeArquivo, byte[] Conteudo);

/// <summary>Documento vencido ou vencendo (painel inicial).</summary>
public sealed record AlertaDocumentoEmpresa(
    Guid EmpresaId,
    string Empresa,
    string Titulo,
    DateOnly ValidaAte,
    SituacaoDocumentoEmpresa Situacao,
    string SituacaoTexto);

public sealed record ResumoDocumentosEmpresaResponse(int Vencidos, int VenceEmBreve, IReadOnlyList<AlertaDocumentoEmpresa> Itens);

/// <summary>
/// DOC1: documentos da Empresa (certidões, cartão CNPJ, contrato social).
/// Tudo dentro da conta atual (filtro por TenantId); a Empresa tem de ser
/// da conta.
/// </summary>
public interface IDocumentosEmpresaUseCase
{
    Task<ListaDocumentosEmpresaResponse> ListarAsync(Guid empresaId, CancellationToken cancellationToken);

    /// <summary>Todas as versões do mesmo "lugar", da mais nova para a mais antiga.</summary>
    Task<IReadOnlyList<VersaoDocumentoEmpresaResponse>> HistoricoAsync(
        Guid empresaId, TipoDocumentoEmpresa tipo, string? nomePersonalizado, CancellationToken cancellationToken);

    /// <summary>Cada envio é uma versão nova (a anterior vai para o histórico). Barra se o plano não libera o recurso.</summary>
    Task<VersaoDocumentoEmpresaResponse> EnviarAsync(EnviarDocumentoEmpresaRequest request, ArquivoDocumentoEmpresa arquivo, CancellationToken cancellationToken);

    Task<ArquivoDocumentoEmpresa> BaixarAsync(Guid documentoId, CancellationToken cancellationToken);

    /// <summary>Apaga UMA versão (banco e disco). Se era a que valia, a anterior volta a valer.</summary>
    Task ExcluirAsync(Guid documentoId, CancellationToken cancellationToken);

    /// <summary>Vencidos e vencendo em até 15 dias, em todas as Empresas ativas da conta (só a versão que vale).</summary>
    Task<ResumoDocumentosEmpresaResponse> ResumoAsync(CancellationToken cancellationToken);
}

/// <summary>Resultado do aviso de uma Empresa (para log do job).</summary>
public sealed record AvisoDocumentosEmpresaResult(int Avisados, IReadOnlyList<string> Destinatarios, string? Motivo);

/// <summary>
/// Confere os documentos de UMA Empresa (Tenant já definido no escopo) e
/// manda um e-mail por documento que entrou numa faixa ainda não avisada
/// (vence em até 15 dias; vencido). Chave "doc:{id}:{faixa}": cada versão
/// avisa cada faixa uma vez — enviou versão nova, os avisos recomeçam.
/// </summary>
public interface IAvisarDocumentosEmpresaVencendoUseCase
{
    Task<AvisoDocumentosEmpresaResult> ExecutarAsync(Guid empresaId, DateOnly hoje, CancellationToken cancellationToken);
}
