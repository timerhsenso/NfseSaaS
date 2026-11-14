using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NfseSaaS.Application.UseCases.DadosConta;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Tela "Meus dados": exportação de todos os dados da conta (D1, LGPD) e
/// encerramento da conta (D2).
/// Aberta a quem está logado; o serviço confere que é administrador da
/// conta para pedir e baixar (quem não é vê só o aviso).
/// </summary>
[ApiController]
[Authorize]
[Route("api/dados-conta")]
public sealed class DadosContaController : ControllerBase
{
    private readonly IExportacaoDadosUseCase _exportacao;
    private readonly IEncerrarContaUseCase _encerramento;

    public DadosContaController(IExportacaoDadosUseCase exportacao, IEncerrarContaUseCase encerramento)
    {
        _exportacao = exportacao;
        _encerramento = encerramento;
    }

    // ---------- Encerrar a conta (D2) ----------

    [HttpGet("encerramento")]
    public async Task<IActionResult> SituacaoEncerramento(CancellationToken cancellationToken)
        => Ok(await _encerramento.ObterSituacaoAsync(cancellationToken));

    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("encerramento/codigo")]
    public async Task<IActionResult> PedirCodigoEncerramento(CancellationToken cancellationToken)
        => Ok(await _encerramento.SolicitarCodigoAsync(cancellationToken));

    [EnableRateLimiting("AuthSensivel")]
    [HttpPost("encerramento/confirmar")]
    public async Task<IActionResult> ConfirmarEncerramento([FromBody] ConfirmarEncerramentoRequest request, CancellationToken cancellationToken)
        => Ok(await _encerramento.ConfirmarAsync(request, cancellationToken));

    // ---------- Exportação dos dados (D1) ----------

    [HttpGet("exportacoes")]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
        => Ok(await _exportacao.ListarAsync(cancellationToken));

    [RequerConfirmacaoSenha]
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
