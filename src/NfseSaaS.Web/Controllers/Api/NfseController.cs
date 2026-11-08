using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Nfse;
using NfseSaaS.Application.UseCases.NfseEventos;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Consultar)]
[Route("api/nfse")]
public sealed class NfseController : ControllerBase
{
    private readonly IEmitirNfseUseCase _emitirNfse;
    private readonly IListarNfseUseCase _listarNfse;
    private readonly IObterNfsePorIdUseCase _obterNfsePorId;
    private readonly ICancelarNfseUseCase _cancelarNfse;
    private readonly IListarEventosDaNfseUseCase _listarEventosDaNfse;
    private readonly IObterSnapshotFiscalDaNfseUseCase _obterSnapshotFiscalDaNfse;
    private readonly IObterDanfsePdfUseCase _obterDanfsePdf;
    private readonly IObterXmlNfseUseCase _obterXmlNfse;
    private readonly IGerarDanfsePdfLoteUseCase _gerarDanfsePdfLote;
    private readonly IEnviarNfsePorEmailUseCase _enviarNfsePorEmail;

    public NfseController(
        IEmitirNfseUseCase emitirNfse,
        IListarNfseUseCase listarNfse,
        IObterNfsePorIdUseCase obterNfsePorId,
        ICancelarNfseUseCase cancelarNfse,
        IListarEventosDaNfseUseCase listarEventosDaNfse,
        IObterSnapshotFiscalDaNfseUseCase obterSnapshotFiscalDaNfse,
        IObterDanfsePdfUseCase obterDanfsePdf,
        IObterXmlNfseUseCase obterXmlNfse,
        IGerarDanfsePdfLoteUseCase gerarDanfsePdfLote,
        IEnviarNfsePorEmailUseCase enviarNfsePorEmail)
    {
        _emitirNfse = emitirNfse;
        _listarNfse = listarNfse;
        _obterNfsePorId = obterNfsePorId;
        _cancelarNfse = cancelarNfse;
        _listarEventosDaNfse = listarEventosDaNfse;
        _obterSnapshotFiscalDaNfse = obterSnapshotFiscalDaNfse;
        _obterDanfsePdf = obterDanfsePdf;
        _obterXmlNfse = obterXmlNfse;
        _gerarDanfsePdfLote = gerarDanfsePdfLote;
        _enviarNfsePorEmail = enviarNfsePorEmail;
    }

    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("emitir")]
    public async Task<IActionResult> Emitir([FromBody] EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _emitirNfse.ExecutarAsync(request, cancellationToken);
        return Ok(resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId = null,
        [FromQuery] NfseStatus? status = null,
        [FromQuery] Guid? clienteId = null,
        [FromQuery] DateOnly? dataInicio = null,
        [FromQuery] DateOnly? dataFim = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarNfse.ExecutarAsync(
            new ListarNfseRequest(empresaId, status, clienteId, dataInicio, dataFim, page, pageSize), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var nfse = await _obterNfsePorId.ExecutarAsync(id, cancellationToken);
        return Ok(nfse);
    }

    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Excluir)]
    [HttpPost("{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _cancelarNfse.ExecutarAsync(id, request, cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}/eventos")]
    public async Task<IActionResult> ListarEventos(Guid id, CancellationToken cancellationToken)
    {
        var eventos = await _listarEventosDaNfse.ExecutarAsync(id, cancellationToken);
        return Ok(eventos);
    }

    [HttpGet("{id:guid}/snapshot-fiscal")]
    public async Task<IActionResult> ObterSnapshotFiscal(Guid id, CancellationToken cancellationToken)
    {
        var snapshot = await _obterSnapshotFiscalDaNfse.ExecutarAsync(id, cancellationToken);
        return Ok(snapshot);
    }

    [HttpGet("{id:guid}/danfse-pdf")]
    public async Task<IActionResult> ObterDanfsePdf(Guid id, CancellationToken cancellationToken)
    {
        var danfse = await _obterDanfsePdf.ExecutarAsync(id, cancellationToken);
        return File(danfse.Bytes, danfse.ContentType, danfse.NomeArquivo);
    }

    /// <summary>XML da NFS-e autorizada (o arquivo fiscal que o contador importa), como foi guardado na autorização.</summary>
    [HttpGet("{id:guid}/xml")]
    public async Task<IActionResult> ObterXml(Guid id, CancellationToken cancellationToken)
    {
        var xml = await _obterXmlNfse.ExecutarAsync(id, cancellationToken);
        return File(xml.Bytes, "application/xml", xml.NomeArquivo);
    }

    /// <summary>
    /// Manda (de novo) a nota autorizada, com PDF e XML, para o e-mail do
    /// tomador — mesmo com o envio automático desligado no cliente. Entra
    /// na fila; o resultado aparece na tela de E-mails.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("{id:guid}/enviar-email")]
    public async Task<IActionResult> EnviarPorEmail(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _enviarNfsePorEmail.ExecutarAsync(id, automatico: false, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>Ids que não tinham DANFSe disponível voltam no header X-Nfse-Ids-Ignorados (CSV) — não aborta o lote inteiro por causa de 1 nota sem PDF.</summary>
    [HttpPost("danfse-pdf/lote")]
    public async Task<IActionResult> ObterDanfsePdfLote([FromBody] GerarDanfsePdfLoteRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _gerarDanfsePdfLote.ExecutarAsync(request, cancellationToken);

        if (resultado.IdsIgnorados.Count > 0)
            Response.Headers.Append("X-Nfse-Ids-Ignorados", string.Join(',', resultado.IdsIgnorados));

        return File(resultado.ZipBytes, "application/zip", resultado.NomeArquivoZip);
    }
}
