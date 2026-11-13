using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.DocumentosEmpresa;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// DOC1: documentos da Empresa (certidões, cartão CNPJ, contrato social).
/// Mesma permissão da tela Empresas: Consultar vê e baixa; Alterar envia e
/// exclui. O recurso do plano ("documentos-empresa") é conferido no envio.
/// </summary>
[ApiController]
[RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Consultar)]
[Route("api/documentos-empresa")]
public sealed class DocumentosEmpresaController : ControllerBase
{
    private readonly IDocumentosEmpresaUseCase _documentos;

    public DocumentosEmpresaController(IDocumentosEmpresaUseCase documentos)
    {
        _documentos = documentos;
    }

    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] Guid empresaId, CancellationToken cancellationToken)
        => Ok(await _documentos.ListarAsync(empresaId, cancellationToken));

    [HttpGet("historico")]
    public async Task<IActionResult> Historico([FromQuery] Guid empresaId, [FromQuery] TipoDocumentoEmpresa tipo, [FromQuery] string? nome, CancellationToken cancellationToken)
        => Ok(await _documentos.HistoricoAsync(empresaId, tipo, nome, cancellationToken));

    /// <summary>Painel inicial: vencidos e vencendo em até 15 dias, em todas as empresas da conta.</summary>
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo(CancellationToken cancellationToken)
        => Ok(await _documentos.ResumoAsync(cancellationToken));

    /// <summary>multipart/form-data: "arquivo" (PDF até 5 MB) e os dados do documento.</summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Enviar(
        [FromForm] Guid empresaId,
        [FromForm] TipoDocumentoEmpresa tipo,
        [FromForm] string? nomePersonalizado,
        [FromForm] DateOnly? dataEmissao,
        [FromForm] DateOnly? validaAte,
        [FromForm] string? observacao,
        IFormFile? arquivo,
        CancellationToken cancellationToken)
    {
        byte[] conteudo = [];
        if (arquivo is not null)
        {
            using var memoria = new MemoryStream();
            await arquivo.CopyToAsync(memoria, cancellationToken);
            conteudo = memoria.ToArray();
        }

        var versao = await _documentos.EnviarAsync(
            new EnviarDocumentoEmpresaRequest(empresaId, tipo, nomePersonalizado, dataEmissao, validaAte, observacao),
            new ArquivoDocumentoEmpresa(arquivo?.FileName ?? string.Empty, conteudo),
            cancellationToken);
        return Ok(versao);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken cancellationToken)
    {
        var arquivo = await _documentos.BaixarAsync(id, cancellationToken);
        return File(arquivo.Conteudo, "application/pdf", arquivo.NomeArquivo);
    }

    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _documentos.ExcluirAsync(id, cancellationToken);
        return NoContent();
    }
}
