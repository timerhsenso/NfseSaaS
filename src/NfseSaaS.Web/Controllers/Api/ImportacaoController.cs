using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.Exceptions;
using NfseSaaS.Application.UseCases.Importacao;
using NfseSaaS.Web.Authorization;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// IM1: importar clientes e contratos por planilha. Quem pode incluir
/// clientes ou contratos usa a tela; cada linha confere a permissão da sua
/// aba (incluir clientes, alterar clientes para atualizar, incluir contratos).
/// </summary>
[ApiController]
[Authorize]
[Route("api/importacao")]
public sealed class ImportacaoController : ControllerBase
{
    private const string TipoXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IImportacaoPlanilhaUseCase _importacao;

    public ImportacaoController(IImportacaoPlanilhaUseCase importacao) => _importacao = importacao;

    [HttpGet("modelo")]
    public async Task<IActionResult> Modelo([FromQuery] Guid empresaId, CancellationToken cancellationToken)
    {
        if (!PodeImportar())
            return Forbid();
        var arquivo = await _importacao.ModeloAsync(empresaId, cancellationToken);
        return File(arquivo, TipoXlsx, "modelo-importacao-micoempresa.xlsx");
    }

    /// <summary>multipart/form-data: "arquivo" (.xlsx ou .csv, até 5 MB), "empresaId" e "atualizarExistentes". Não grava nada.</summary>
    [HttpPost("conferir")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Conferir(
        [FromForm] Guid empresaId, [FromForm] bool atualizarExistentes, IFormFile? arquivo, CancellationToken cancellationToken)
    {
        if (!PodeImportar())
            return Forbid();
        var lido = await LerArquivoAsync(arquivo, cancellationToken);
        return Ok(await _importacao.ConferirAsync(empresaId, lido, new OpcoesImportacao(atualizarExistentes), Permissoes(), cancellationToken));
    }

    /// <summary>Mesmo envio da conferência: confere de novo e grava as linhas sem erro.</summary>
    [HttpPost("gravar")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Gravar(
        [FromForm] Guid empresaId, [FromForm] bool atualizarExistentes, IFormFile? arquivo, CancellationToken cancellationToken)
    {
        if (!PodeImportar())
            return Forbid();
        var lido = await LerArquivoAsync(arquivo, cancellationToken);
        return Ok(await _importacao.GravarAsync(empresaId, lido, new OpcoesImportacao(atualizarExistentes), Permissoes(), cancellationToken));
    }

    /// <summary>As linhas com erro (vindas da conferência) numa planilha para corrigir e mandar de novo.</summary>
    [HttpPost("relatorio-erros")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public IActionResult RelatorioErros([FromBody] RelatorioErrosImportacaoRequest request)
    {
        if (!PodeImportar())
            return Forbid();
        return File(_importacao.RelatorioErros(request), TipoXlsx, "linhas-com-erro.xlsx");
    }

    private bool PodeImportar() =>
        User.PodeIncluir(TelaCatalogo.Clientes) || User.PodeIncluir(TelaCatalogo.Contratos);

    private PermissoesImportacao Permissoes() => new(
        User.PodeIncluir(TelaCatalogo.Clientes),
        User.PodeAlterar(TelaCatalogo.Clientes),
        User.PodeIncluir(TelaCatalogo.Contratos));

    private static async Task<ArquivoImportacao> LerArquivoAsync(IFormFile? arquivo, CancellationToken cancellationToken)
    {
        if (arquivo is null || arquivo.Length == 0)
            throw new RegraNegocioException("Escolha a planilha (.xlsx ou .csv).");
        if (arquivo.Length > RegrasImportacao.TamanhoMaximoArquivo)
            throw new RegraNegocioException(RegrasImportacao.MensagemArquivoGrande);

        using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, cancellationToken);
        return new ArquivoImportacao(Path.GetFileName(arquivo.FileName), memoria.ToArray());
    }
}
