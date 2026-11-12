using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
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
    private readonly IObterDadosSubstituicaoUseCase _obterDadosSubstituicao;

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
        IEnviarNfsePorEmailUseCase enviarNfsePorEmail,
        IObterDadosSubstituicaoUseCase obterDadosSubstituicao)
    {
        _obterDadosSubstituicao = obterDadosSubstituicao;
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
        // Substituir cancela a original: tem rota própria, com a permissão de cancelar.
        if (request.SubstituirNfseId is not null)
            throw new RegraNegocioException("Para substituir uma nota, use \"Substituir\" na lista de notas.");

        var resultado = await _emitirNfse.ExecutarAsync(request, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>C7: motivos oficiais e campos que o regime da empresa não deixa mudar.</summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Excluir)]
    [HttpGet("{id:guid}/substituicao")]
    public async Task<IActionResult> DadosSubstituicao(Guid id, CancellationToken cancellationToken)
        => Ok(await _obterDadosSubstituicao.ExecutarAsync(id, cancellationToken));

    /// <summary>
    /// C7: emite a substituta da nota {id}; autorizada, a SEFIN cancela a
    /// original (evento 105102). Mesma permissão do cancelamento.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Excluir)]
    [HttpPost("{id:guid}/substituir")]
    public async Task<IActionResult> Substituir(Guid id, [FromBody] EmitirNfseRequest request, CancellationToken cancellationToken)
    {
        var resultado = await _emitirNfse.ExecutarAsync(request with { SubstituirNfseId = id, ReenvioDeNfseId = null }, cancellationToken);
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
    /// na fila; o resultado aparece na tela de E-mails. Corpo opcional:
    /// assunto e mensagem escritos no modal, só para este envio (sem corpo
    /// = o modelo da empresa/conta).
    /// </summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("{id:guid}/enviar-email")]
    public async Task<IActionResult> EnviarPorEmail(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TextoEmailNota? texto, CancellationToken cancellationToken)
    {
        var resultado = await _enviarNfsePorEmail.ExecutarAsync(id, automatico: false, texto, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>Modal de envio: destinatário, cópias e o texto já preenchido pelo modelo.</summary>
    [HttpGet("{id:guid}/email-previa")]
    public async Task<IActionResult> PreviaEmail(Guid id, [FromServices] IEnvioEmailNfseUseCase envioEmail, CancellationToken cancellationToken)
        => Ok(await envioEmail.PreviaAsync(id, cancellationToken));

    /// <summary>Resumo antes de enviar várias notas (quantas vão, já enviadas, sem e-mail, não autorizadas).</summary>
    [HttpPost("enviar-email/lote/previa")]
    public async Task<IActionResult> PreviaEnvioEmailLote([FromBody] EnvioEmailLoteRequest request, [FromServices] IEnvioEmailNfseUseCase envioEmail, CancellationToken cancellationToken)
        => Ok(await envioEmail.PreviaLoteAsync(request, cancellationToken));

    /// <summary>Envia por e-mail as notas selecionadas: um e-mail por nota, com o modelo de cada empresa.</summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("enviar-email/lote")]
    public async Task<IActionResult> EnviarEmailLote([FromBody] EnvioEmailLoteRequest request, [FromServices] IEnvioEmailNfseUseCase envioEmail, CancellationToken cancellationToken)
        => Ok(await envioEmail.EnviarLoteAsync(request, cancellationToken));

    /// <summary>
    /// XML do mês: ZIP com o XML de todas as notas autorizadas e canceladas da
    /// empresa no mês de competência (para o contador). Sem ambiente = o atual da empresa.
    /// </summary>
    [HttpGet("xml-mes")]
    public async Task<IActionResult> BaixarXmlMes(
        [FromQuery] Guid empresaId,
        [FromQuery] DateOnly competencia,
        [FromQuery] NfseSaaS.Domain.Enums.TipoAmbiente? ambiente,
        [FromServices] IBaixarXmlMesUseCase baixarXmlMes,
        CancellationToken cancellationToken)
    {
        var resultado = await baixarXmlMes.ExecutarAsync(empresaId, competencia, ambiente, cancellationToken);
        Response.Headers.Append("X-Quantidade-Notas", resultado.Quantidade.ToString());
        return File(resultado.ZipBytes, "application/zip", resultado.NomeArquivoZip);
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
