using NfseSaaS.Domain.Enums;

namespace NfseSaaS.Application.UseCases.DocumentosEmpresa;

/// <summary>Uma opção do cadastro do cliente: documento que pode ser exigido e como está na empresa atual.</summary>
/// <param name="Situacao">Na empresa escolhida no topo (nulo quando a tela não informou a empresa).</param>
public sealed record OpcaoDocumentoExigido(
    int Tipo,
    string NomeChave,
    string Titulo,
    bool Exigido,
    SituacaoDocumentoExigido? Situacao,
    string? SituacaoTexto);

public sealed record DocumentosExigidosClienteResponse(
    Guid? ClienteId,
    AcaoDocumentoPendente AcaoPendente,
    IReadOnlyList<OpcaoDocumentoExigido> Opcoes);

public sealed record ChaveDocumentoRequest(int Tipo, string? NomeChave);

public sealed record SalvarDocumentosExigidosRequest(IReadOnlyList<ChaveDocumentoRequest>? Documentos, AcaoDocumentoPendente AcaoPendente);

/// <summary>Documento na prévia do e-mail da nota (modal de envio).</summary>
/// <param name="Id">Versão que vale (nula = faltando).</param>
public sealed record DocumentoEmailPrevia(
    Guid? Id,
    string Titulo,
    SituacaoDocumentoExigido Situacao,
    string SituacaoTexto,
    string? NomeArquivo,
    long TamanhoBytes);

/// <summary>
/// DOC2: cadastro "Documentos que vão junto com a nota" do cliente e a
/// leitura dos documentos que valem na empresa (usada pelo envio do e-mail).
/// </summary>
public interface IDocumentosExigidosClienteUseCase
{
    /// <summary>Opções para o cadastro (tipos fixos, "Outros" da empresa e os já escolhidos). clienteId nulo = cliente novo.</summary>
    Task<DocumentosExigidosClienteResponse> ObterAsync(Guid? clienteId, Guid? empresaId, CancellationToken cancellationToken);

    Task SalvarAsync(Guid clienteId, SalvarDocumentosExigidosRequest request, CancellationToken cancellationToken);
}
