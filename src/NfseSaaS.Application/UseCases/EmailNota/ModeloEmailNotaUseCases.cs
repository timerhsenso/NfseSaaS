namespace NfseSaaS.Application.UseCases.EmailNota;

/// <summary>Uma empresa da conta, para escolher de qual modelo se fala na tela.</summary>
public sealed record EmpresaModeloEmailNota(Guid Id, string Nome, string? Email, bool Personalizado);

/// <summary>
/// Seção "E-mail da nota" (Configurações da conta). Sem EmpresaId = modelo
/// da conta. Com EmpresaId: Personalizado=false quer dizer que a empresa usa
/// o modelo da conta (e os campos mostram o da conta, para servir de base).
/// </summary>
public sealed record ModeloEmailNotaResponse(
    Guid? EmpresaId,
    bool Personalizado,
    string Assunto,
    string Mensagem,
    string? ResponderPara,
    string? CopiaOcultaPara,
    bool OcultarDivulgacao,
    bool PodeOcultarDivulgacao,
    bool PodeAlterar,
    string? EmailPadraoResposta,
    IReadOnlyList<CampoModeloEmailNota> Campos,
    IReadOnlyList<EmpresaModeloEmailNota> Empresas)
{
    /// <summary>"Voltar ao texto padrão" da tela.</summary>
    public string AssuntoPadrao => RegrasModeloEmailNota.AssuntoPadrao;

    public string MensagemPadrao => RegrasModeloEmailNota.MensagemPadrao;
}

/// <param name="EmpresaId">Nulo = modelo da conta.</param>
/// <param name="Personalizar">Só com EmpresaId: false apaga o modelo da empresa (volta a usar o da conta).</param>
public sealed record SalvarModeloEmailNotaRequest(
    Guid? EmpresaId,
    bool Personalizar,
    string? Assunto,
    string? Mensagem,
    string? ResponderPara,
    string? CopiaOcultaPara,
    bool OcultarDivulgacao);

/// <summary>"Mandar um teste para mim": o texto da tela, com dados de exemplo, para o e-mail de quem está logado.</summary>
public sealed record EnviarTesteModeloEmailNotaRequest(Guid? EmpresaId, string? Assunto, string? Mensagem);

public interface IModeloEmailNotaUseCase
{
    Task<ModeloEmailNotaResponse> ObterAsync(Guid? empresaId, CancellationToken cancellationToken);

    /// <summary>Só administrador da conta.</summary>
    Task<ModeloEmailNotaResponse> SalvarAsync(SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken);

    /// <summary>Devolve o e-mail para onde o teste foi.</summary>
    Task<string> EnviarTesteAsync(EnviarTesteModeloEmailNotaRequest request, CancellationToken cancellationToken);

    /// <summary>O modelo que vale para a empresa no envio (o dela, o da conta ou o padrão).</summary>
    Task<ModeloEmailNotaEfetivo> ResolverAsync(Guid tenantId, Guid empresaId, CancellationToken cancellationToken);
}
