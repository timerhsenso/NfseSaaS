using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Contratos;
using NfseSaaS.Application.UseCases.DocumentosContrato;
using NfseSaaS.Application.UseCases.HistoricoContrato;
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
    private readonly IDesativarContratoUseCase _desativarContrato;
    private readonly IReativarContratoUseCase _reativarContrato;
    private readonly IExcluirContratoUseCase _excluirContrato;
    private readonly IRegistrarReajusteUseCase _registrarReajuste;
    private readonly IListarReajustesUseCase _listarReajustes;
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
        IDesativarContratoUseCase desativarContrato,
        IReativarContratoUseCase reativarContrato,
        IExcluirContratoUseCase excluirContrato,
        IRegistrarReajusteUseCase registrarReajuste,
        IListarReajustesUseCase listarReajustes,
        IUploadDocumentoContratoUseCase uploadDocumento,
        IListarDocumentosContratoUseCase listarDocumentos,
        IExcluirDocumentoContratoUseCase excluirDocumento,
        IObterDocumentoContratoParaDownloadUseCase obterDocumentoParaDownload,
        IObterHistoricoContratoUseCase obterHistorico)
    {
        _cadastrarContrato = cadastrarContrato;
        _listarContratos = listarContratos;
        _obterContratoPorId = obterContratoPorId;
        _atualizarContrato = atualizarContrato;
        _desativarContrato = desativarContrato;
        _reativarContrato = reativarContrato;
        _excluirContrato = excluirContrato;
        _registrarReajuste = registrarReajuste;
        _listarReajustes = listarReajustes;
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

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar.</summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarContrato.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarContrato.ExecutarAsync(id, cancellationToken);
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

    /// <summary>Único caminho pra mudar o valor de um Contrato depois de criado — grava histórico (ver RegistrarReajusteUseCase).</summary>
    [RequerPermissao(TelaCatalogo.Contratos, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reajustes")]
    public async Task<IActionResult> RegistrarReajuste(Guid id, [FromBody] RegistrarReajusteRequest request, CancellationToken cancellationToken)
    {
        var reajusteId = await _registrarReajuste.ExecutarAsync(id, request, cancellationToken);
        return Ok(new { reajusteId });
    }

    [HttpGet("{id:guid}/reajustes")]
    public async Task<IActionResult> ListarReajustes(Guid id, CancellationToken cancellationToken)
    {
        var reajustes = await _listarReajustes.ExecutarAsync(id, cancellationToken);
        return Ok(reajustes);
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
