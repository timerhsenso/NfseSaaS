using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.UseCases.DadosConta;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Tela "Meus dados": exportação de todos os dados da conta (D1, LGPD).
/// Aberta a quem está logado; o serviço confere que é administrador da
/// conta para pedir e baixar (quem não é vê só o aviso).
/// </summary>
[ApiController]
[Authorize]
[Route("api/dados-conta")]
public sealed class DadosContaController : ControllerBase
{
    private readonly IExportacaoDadosUseCase _exportacao;

    public DadosContaController(IExportacaoDadosUseCase exportacao)
    {
        _exportacao = exportacao;
    }

    [HttpGet("exportacoes")]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
        => Ok(await _exportacao.ListarAsync(cancellationToken));

    [HttpPost("exportacoes")]
    public async Task<IActionResult> Solicitar(CancellationToken cancellationToken)
        => Ok(await _exportacao.SolicitarAsync(cancellationToken));

    [HttpGet("exportacoes/{id:guid}/arquivo")]
    public async Task<IActionResult> Baixar(Guid id, CancellationToken cancellationToken)
    {
        var arquivo = await _exportacao.BaixarAsync(id, cancellationToken);
        return PhysicalFile(arquivo.Caminho, "application/zip", arquivo.NomeArquivo, enableRangeProcessing: true);
    }
}
