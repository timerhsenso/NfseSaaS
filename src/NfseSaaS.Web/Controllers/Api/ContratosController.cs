using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Application.UseCases.HistoricoContrato;
using NfseSaaS.Application.UseCases.HistoricoValoresContrato;
using NfseSaaS.Application.UseCases.ReajustesContrato;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Consultar)]
[Route("api/contratos")]
public sealed class ContratosController : ControllerBase
{
    private readonly ICadastrarContratoUseCase _cadastrarContrato;
    private readonly IListarContratosUseCase _listarContratos;
    private readonly IObterContratoPorIdUseCase _obterContratoPorId;
    private readonly IAtualizarContratoUseCase _atualizarContrato;
    private readonly IExcluirContratoUseCase _excluirContrato;
    private readonly IRegistrarReajusteUseCase _registrarReajuste;
    private readonly ISimularReajusteContratoUseCase _simularReajuste;
    private readonly IObterProximoNumeroContratoUseCase _proximoNumero;
    private readonly IListarHistoricoValoresContratoUseCase _listarHistoricoValores;
    private readonly IUploadDocumentoContratoUseCase _uploadDocumento;
    private readonly IListarDocumentosContratoUseCase _listarDocumentos;
    private readonly IExcluirDocumentoContratoUseCase _excluirDocumento;
    private readonly IObterDocumentoContratoParaDownloadUseCase _obterDocumentoParaDownload;
    private readonly IObterHistoricoContratoUseCase _obterHistorico;

    public ContratosController(
        ICadastrarContratoUseCase cadastrarContrato,
        IListarContratosUseCase listarContratos,
        IObterContratoPorIdUseCase obterContratoPorId,
        IAtualizarContratoUseCase atualizarContrato,
        IExcluirContratoUseCase excluirContrato,
        IRegistrarReajusteUseCase registrarReajuste,
        IListarHistoricoValoresContratoUseCase listarHistoricoValores,
        IUploadDocumentoContratoUseCase uploadDocumento,
        IListarDocumentosContratoUseCase listarDocumentos,
        IExcluirDocumentoContratoUseCase excluirDocumento,
        IObterDocumentoContratoParaDownloadUseCase obterDocumentoParaDownload,
        IObterHistoricoContratoUseCase obterHistorico,
        ISimularReajusteContratoUseCase simularReajuste,
        IObterProximoNumeroContratoUseCase proximoNumero)
    {
        _proximoNumero = proximoNumero;
        _simularReajuste = simularReajuste;
        _cadastrarContrato = cadastrarContrato;
        _listarContratos = listarContratos;
        _obterContratoPorId = obterContratoPorId;
        _atualizarContrato = atualizarContrato;
        _excluirContrato = excluirContrato;
        _registrarReajuste = registrarReajuste;
        _listarHistoricoValores = listarHistoricoValores;
        _uploadDocumento = uploadDocumento;
        _listarDocumentos = listarDocumentos;
        _excluirDocumento = excluirDocumento;
        _obterDocumentoParaDownload = obterDocumentoParaDownload;
        _obterHistorico = obterHistorico;
    }

    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Incluir)]
    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarContratoRequest request, CancellationToken cancellationToken)
    {
        var contratoId = await _cadastrarContrato.ExecutarAsync(request, cancellationToken);
        return Ok(new { contratoId });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? busca = null,
        [FromQuery] bool incluirInativos = false,
        [FromQuery] Guid? clienteId = null,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarContratos.ExecutarAsync(
            new ListarContratosRequest(empresaId, page, pageSize, busca, incluirInativos, clienteId), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var contrato = await _obterContratoPorId.ExecutarAsync(id, cancellationToken);
        return Ok(contrato);
    }

    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarContratoRequest request, CancellationToken cancellationToken)
    {
        await _atualizarContrato.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — bloqueada pelo banco se houver vínculo futuro (ver ExcluirContratoUseCase).</summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Excluir)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirContrato.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Registra um reajuste manual — vira um evento do Histórico de valores (ver RegistrarReajusteUseCase).</summary>
    /// <summary>Número proposto para o próximo contrato da empresa ("ano/NNNN").</summary>
    [HttpGet("proximo-numero")]
    public async Task<IActionResult> ProximoNumero([FromQuery] Guid empresaId, CancellationToken cancellationToken)
    {
        var numero = await _proximoNumero.ExecutarAsync(empresaId, cancellationToken);
        return Ok(new { numero });
    }

    /// <summary>
    /// Simula o reajuste pelo índice (acumulado de 12 meses), sem gravar —
    /// a tela mostra o percentual e o valor novo antes de "Registrar".
    /// </summary>
    [HttpGet("{id:guid}/reajustes/simulacao")]
    public async Task<IActionResult> SimularReajuste(
        Guid id,
        [FromQuery] IndiceReajusteContrato? indice,
        [FromQuery] DateOnly? data,
        [FromQuery] bool? aplicarNegativo,
        CancellationToken cancellationToken)
    {
        var resultado = await _simularReajuste.ExecutarAsync(id, new SimularReajusteRequest(indice, data, aplicarNegativo), cancellationToken);
        return Ok(resultado);
    }

    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reajustes")]
    public async Task<IActionResult> RegistrarReajuste(Guid id, [FromBody] RegistrarReajusteRequest request, CancellationToken cancellationToken)
    {
        var reajusteId = await _registrarReajuste.ExecutarAsync(id, request, cancellationToken);
        return Ok(new { reajusteId });
    }

    /// <summary>Aprova (pelo índice ou negociado) ou recusa um reajuste de aniversário aguardando aprovação.</summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reajustes/{movimentacaoId:guid}/decisao")]
    public async Task<IActionResult> DecidirReajuste(
        Guid id, Guid movimentacaoId, [FromBody] DecidirReajusteRequest request,
        [FromServices] IDecidirReajusteUseCase decidirReajuste, CancellationToken cancellationToken)
    {
        await decidirReajuste.ExecutarAsync(id, movimentacaoId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Roda agora, para a Empresa, o mesmo cálculo do job diário dos
    /// reajustes de aniversário (10:10) — para não esperar o dia seguinte.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("reajustes/verificar")]
    public async Task<IActionResult> VerificarReajustes(
        [FromQuery] Guid empresaId, [FromServices] IGerarReajustesAniversarioUseCase gerarReajustes, CancellationToken cancellationToken)
        => Ok(await gerarReajustes.ExecutarAsync(empresaId, cancellationToken));

    /// <summary>Histórico de valores do Contrato: valor inicial, reajustes e alterações de escopo, do mais recente pro mais antigo.</summary>
    [HttpGet("{id:guid}/historico-valores")]
    public async Task<IActionResult> ListarHistoricoValores(Guid id, CancellationToken cancellationToken)
    {
        var eventos = await _listarHistoricoValores.ExecutarAsync(id, cancellationToken);
        return Ok(eventos);
    }

    /// <summary>multipart/form-data: campo "arquivo" (.pdf/.doc/.docx) — mesmo padrão do upload de certificado (EmpresasController).</summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Incluir)]
    [HttpPost("{id:guid}/documentos")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB por arquivo
    public async Task<IActionResult> UploadDocumento(Guid id, IFormFile arquivo, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await arquivo.CopyToAsync(memoryStream, cancellationToken);

        var documento = await _uploadDocumento.ExecutarAsync(id, arquivo.FileName, memoryStream.ToArray(), cancellationToken);
        return Ok(documento);
    }

    [HttpGet("{id:guid}/documentos")]
    public async Task<IActionResult> ListarDocumentos(Guid id, CancellationToken cancellationToken)
    {
        var documentos = await _listarDocumentos.ExecutarAsync(id, cancellationToken);
        return Ok(documentos);
    }

    [HttpGet("documentos/{documentoId:guid}/download")]
    public async Task<IActionResult> BaixarDocumento(Guid documentoId, CancellationToken cancellationToken)
    {
        var arquivo = await _obterDocumentoParaDownload.ExecutarAsync(documentoId, cancellationToken);
        return File(arquivo.Conteudo, arquivo.ContentType, arquivo.NomeOriginal);
    }

    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Excluir)]
    [HttpDelete("documentos/{documentoId:guid}")]
    public async Task<IActionResult> ExcluirDocumento(Guid documentoId, CancellationToken cancellationToken)
    {
        await _excluirDocumento.ExecutarAsync(documentoId, cancellationToken);
        return NoContent();
    }

    /// <summary>Timeline combinando AuditLog + ReajusteContrato deste Contrato (ver ObterHistoricoContratoUseCase).</summary>
    [HttpGet("{id:guid}/historico")]
    public async Task<IActionResult> ObterHistorico(Guid id, CancellationToken cancellationToken)
    {
        var historico = await _obterHistorico.ExecutarAsync(id, cancellationToken);
        return Ok(historico);
    }
}
