namespace NfseSaaS.Application.UseCases.EmailNota;

/// <summary>Um modelo na tela "Modelos de e-mail", com quem o usa.</summary>
/// <param name="Empresas">Nomes das empresas que escolheram este modelo.</param>
/// <param name="Clientes">Quantos clientes escolheram este modelo.</param>
public sealed record ModeloEmailNotaItem(
    Guid Id,
    string Nome,
    bool Padrao,
    string Assunto,
    string Mensagem,
    string? ResponderPara,
    string? CopiaOcultaPara,
    bool OcultarDivulgacao,
    IReadOnlyList<string> Empresas,
    int Clientes);

/// <summary>Tela "Modelos de e-mail".</summary>
/// <param name="PodeAlterar">Administrador da conta.</param>
/// <param name="PodeOcultarDivulgacao">Plano Escritório: pode tirar o convite do micoempresa do rodapé.</param>
public sealed record ModelosEmailNotaResponse(
    IReadOnlyList<ModeloEmailNotaItem> Modelos,
    IReadOnlyList<CampoModeloEmailNota> Campos,
    bool PodeAlterar,
    bool PodeOcultarDivulgacao)
{
    /// <summary>"Voltar ao texto padrão" da tela.</summary>
    public string AssuntoPadrao => RegrasModeloEmailNota.AssuntoPadrao;

    public string MensagemPadrao => RegrasModeloEmailNota.MensagemPadrao;

    public int MaximoModelos => RegrasModeloEmailNota.MaximoModelos;
}

/// <summary>Opção dos campos "Modelo do e-mail da nota" (Empresa, Cliente, modal de envio).</summary>
public sealed record OpcaoModeloEmailNota(Guid Id, string Nome, bool Padrao);

public sealed record SalvarModeloEmailNotaRequest(
    string? Nome,
    string? Assunto,
    string? Mensagem,
    string? ResponderPara,
    string? CopiaOcultaPara,
    bool OcultarDivulgacao);

/// <summary>"Mandar um teste para mim": o texto da tela, com dados de exemplo.</summary>
public sealed record EnviarTesteModeloEmailNotaRequest(string? Assunto, string? Mensagem, string? ResponderPara, bool OcultarDivulgacao);

/// <summary>Escolha do modelo numa Empresa ou num Cliente. Nulo = usar o padrão (ou o da empresa, no cliente).</summary>
public sealed record DefinirModeloEmailNotaRequest(Guid? ModeloId);

public interface IModeloEmailNotaUseCase
{
    /// <summary>Lista os modelos. Conta sem nenhum ganha o "Padrão" com o texto do sistema.</summary>
    Task<ModelosEmailNotaResponse> ListarAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OpcaoModeloEmailNota>> OpcoesAsync(CancellationToken cancellationToken);

    /// <summary>Os de escrita (criar, alterar, duplicar, padrão, excluir): só administrador.</summary>
    Task<Guid> CriarAsync(SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken);

    Task AtualizarAsync(Guid id, SalvarModeloEmailNotaRequest request, CancellationToken cancellationToken);

    Task<Guid> DuplicarAsync(Guid id, CancellationToken cancellationToken);

    Task DefinirPadraoAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>O padrão não sai. Quem usava o excluído volta para o padrão.</summary>
    Task ExcluirAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Devolve o e-mail para onde o teste foi.</summary>
    Task<string> EnviarTesteAsync(EnviarTesteModeloEmailNotaRequest request, CancellationToken cancellationToken);

    Task DefinirDaEmpresaAsync(Guid empresaId, DefinirModeloEmailNotaRequest request, CancellationToken cancellationToken);

    Task DefinirDoClienteAsync(Guid clienteId, DefinirModeloEmailNotaRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// O modelo que vale para uma nota: <paramref name="modeloEscolhidoId"/>
    /// (escolhido no modal), senão o do cliente, o da empresa, o padrão da
    /// conta ou o texto do sistema.
    /// </summary>
    Task<ModeloEmailNotaEfetivo> ResolverAsync(Guid tenantId, Guid empresaId, Guid clienteId, Guid? modeloEscolhidoId, CancellationToken cancellationToken);
}
