using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.UseCases.Cobranca;
using NfseSaaS.Application.UseCases.Indicacao;
using NfseSaaS.Application.UseCases.Plataforma;
using NfseSaaS.Application.UseCases.Recursos;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

/// <summary>
/// Operação da plataforma (dono do SaaS) — enxerga TODAS as contas. Fora da
/// matriz de permissões do tenant: só quem está em
/// Plataforma:UsuariosOperadoresIds (RequerOperadorPlataforma).
/// </summary>
[ApiController]
[RequerOperadorPlataforma]
[Route("api/plataforma")]
public sealed class PlataformaController : ControllerBase
{
    private readonly IListarTenantsPlataformaUseCase _listar;
    private readonly IAlterarBloqueioTenantUseCase _alterarBloqueio;
    private readonly IConsultarProtecaoChavesUseCase _consultarProtecaoChaves;
    private readonly IReprotegerChavesUseCase _reprotegerChaves;
    private readonly IListarPremiosIndicacaoPlataformaUseCase _listarPremios;
    private readonly IReabrirPremioIndicacaoUseCase _reabrirPremio;
    private readonly ISolicitarExclusaoContaUseCase _solicitarExclusao;
    private readonly IConfirmarExclusaoContaUseCase _confirmarExclusao;
    private readonly IAdministrarPlanosUseCase _planos;
    private readonly ISolicitarExclusaoPlanoUseCase _solicitarExclusaoPlano;
    private readonly IConfirmarExclusaoPlanoUseCase _confirmarExclusaoPlano;
    private readonly ICurrentUser _currentUser;
    private readonly ICobrancaOperadorUseCase _cobrancaOperador;
    private readonly IConciliarCobrancaUseCase _conciliarCobranca;
    private readonly IReajusteAssinantesUseCase _reajuste;

    public PlataformaController(
        IListarTenantsPlataformaUseCase listar,
        IAlterarBloqueioTenantUseCase alterarBloqueio,
        IConsultarProtecaoChavesUseCase consultarProtecaoChaves,
        IReprotegerChavesUseCase reprotegerChaves,
        IListarPremiosIndicacaoPlataformaUseCase listarPremios,
        IReabrirPremioIndicacaoUseCase reabrirPremio,
        ISolicitarExclusaoContaUseCase solicitarExclusao,
        IConfirmarExclusaoContaUseCase confirmarExclusao,
        IAdministrarPlanosUseCase planos,
        ISolicitarExclusaoPlanoUseCase solicitarExclusaoPlano,
        IConfirmarExclusaoPlanoUseCase confirmarExclusaoPlano,
        ICurrentUser currentUser,
        ICobrancaOperadorUseCase cobrancaOperador,
        IConciliarCobrancaUseCase conciliarCobranca,
        IReajusteAssinantesUseCase reajuste)
    {
        _reajuste = reajuste;
        _cobrancaOperador = cobrancaOperador;
        _conciliarCobranca = conciliarCobranca;
        _listar = listar;
        _alterarBloqueio = alterarBloqueio;
        _consultarProtecaoChaves = consultarProtecaoChaves;
        _reprotegerChaves = reprotegerChaves;
        _listarPremios = listarPremios;
        _reabrirPremio = reabrirPremio;
        _solicitarExclusao = solicitarExclusao;
        _confirmarExclusao = confirmarExclusao;
        _planos = planos;
        _solicitarExclusaoPlano = solicitarExclusaoPlano;
        _confirmarExclusaoPlano = confirmarExclusaoPlano;
        _currentUser = currentUser;
    }

    [HttpGet("tenants")]
    public async Task<IActionResult> ListarTenants(CancellationToken cancellationToken)
        => Ok(await _listar.ExecutarAsync(cancellationToken));

    [HttpPost("tenants/{id:guid}/suspender")]
    public async Task<IActionResult> Suspender(Guid id, [FromBody] SuspenderTenantRequest request, CancellationToken cancellationToken)
    {
        await _alterarBloqueio.ExecutarAsync(id, true, request.Motivo, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("tenants/{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _alterarBloqueio.ExecutarAsync(id, false, null, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Encerra a conta (D2): cancela a assinatura, fecha o acesso e agenda o
    /// expurgo para daqui a 90 dias. "Reativar" reabre enquanto não apagou.
    /// </summary>
    [HttpPost("tenants/{id:guid}/encerrar")]
    public async Task<IActionResult> Encerrar(
        Guid id,
        [FromBody] NfseSaaS.Application.UseCases.DadosConta.EncerrarContaPlataformaRequest request,
        [FromServices] NfseSaaS.Application.UseCases.DadosConta.IEncerrarContaUseCase encerrar,
        CancellationToken cancellationToken)
    {
        var operadorId = _currentUser.UserId ?? throw new InvalidOperationException("Operador não identificado.");
        return Ok(await encerrar.EncerrarPelaPlataformaAsync(id, request.Motivo, operadorId, cancellationToken));
    }

    /// <summary>Expira a exportação dos dados pronta da conta para ela poder pedir outra antes dos 7 dias.</summary>
    [HttpPost("tenants/{id:guid}/exportacao/liberar")]
    public async Task<IActionResult> LiberarExportacao(
        Guid id,
        [FromServices] NfseSaaS.Application.UseCases.DadosConta.IExportacaoDadosUseCase exportacao,
        CancellationToken cancellationToken)
    {
        var operadorId = _currentUser.UserId ?? throw new InvalidOperationException("Operador não identificado.");
        await exportacao.LiberarNovaPelaPlataformaAsync(id, operadorId, cancellationToken);
        return NoContent();
    }

    // ---------- Cobrança da conta (prorrogar/liberar teste, isentar) ----------

    /// <summary>Confere a assinatura da conta com o Asaas agora (a mesma conciliação do job diário).</summary>
    [HttpPost("tenants/{id:guid}/cobranca/conciliar")]
    public async Task<IActionResult> ConciliarCobranca(Guid id, CancellationToken cancellationToken)
        => Ok(await _conciliarCobranca.ConciliarAsync(id, _currentUser.UserId, cancellationToken));

    [HttpPost("tenants/{id:guid}/teste/prorrogar")]
    public async Task<IActionResult> ProrrogarTeste(Guid id, [FromBody] ProrrogarTesteRequest request, CancellationToken cancellationToken)
        => Ok(await _cobrancaOperador.ProrrogarTesteAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpPost("tenants/{id:guid}/teste/liberar")]
    public async Task<IActionResult> LiberarTeste(Guid id, [FromBody] LiberarTesteRequest request, CancellationToken cancellationToken)
        => Ok(await _cobrancaOperador.LiberarTesteAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpPost("tenants/{id:guid}/isencao")]
    public async Task<IActionResult> Isentar(Guid id, [FromBody] IsentarContaRequest request, CancellationToken cancellationToken)
        => Ok(await _cobrancaOperador.IsentarAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpDelete("tenants/{id:guid}/isencao")]
    public async Task<IActionResult> RemoverIsencao(Guid id, CancellationToken cancellationToken)
        => Ok(await _cobrancaOperador.RemoverIsencaoAsync(id, _currentUser.UserId, cancellationToken));

    /// <summary>Exclusão de conta, passo 1: confere as regras e envia o código ao e-mail do operador.</summary>
    [HttpPost("tenants/{id:guid}/exclusao/codigo")]
    public async Task<IActionResult> PedirCodigoExclusao(Guid id, CancellationToken cancellationToken)
        => Ok(await _solicitarExclusao.ExecutarAsync(id, OperadorId(), cancellationToken));

    /// <summary>Exclusão de conta, passo 2: com o código, apaga a conta inteira.</summary>
    [HttpPost("tenants/{id:guid}/exclusao/confirmar")]
    public async Task<IActionResult> ConfirmarExclusao(Guid id, [FromBody] ConfirmarExclusaoContaRequest request, CancellationToken cancellationToken)
    {
        await _confirmarExclusao.ExecutarAsync(id, OperadorId(), request.Codigo, cancellationToken);
        return NoContent();
    }

    private Guid OperadorId() =>
        _currentUser.UserId ?? throw new InvalidOperationException("Operador não identificado.");

    /// <summary>Prêmios do "Indique e ganhe" (os 300 mais recentes).</summary>
    [HttpGet("premios-indicacao")]
    public async Task<IActionResult> PremiosIndicacao(CancellationToken cancellationToken)
        => Ok(await _listarPremios.ExecutarAsync(cancellationToken));

    /// <summary>Devolve um prêmio com erro para a fila (conferir antes no Asaas).</summary>
    [HttpPost("premios-indicacao/{id:guid}/tentar-novamente")]
    public async Task<IActionResult> TentarPremioNovamente(Guid id, CancellationToken cancellationToken)
    {
        await _reabrirPremio.ExecutarAsync(id, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    /// <summary>Situação das chaves do Data Protection (criptografadas ou não).</summary>
    [HttpGet("protecao-chaves")]
    public async Task<IActionResult> ProtecaoChaves(CancellationToken cancellationToken)
        => Ok(await _consultarProtecaoChaves.ExecutarAsync(cancellationToken));

    /// <summary>Regrava os certificados com uma chave nova criptografada e apaga as chaves antigas sem criptografia.</summary>
    [HttpPost("protecao-chaves/reproteger")]
    public async Task<IActionResult> ReprotegerChaves(CancellationToken cancellationToken)
        => Ok(await _reprotegerChaves.ExecutarAsync(_currentUser.UserId, cancellationToken));

    // ---------- PL1: recursos por plano e exceções por conta ----------

    [HttpGet("planos/{id:guid}/recursos")]
    public async Task<IActionResult> RecursosDoPlano(Guid id, [FromServices] IAdministrarRecursosUseCase recursos, CancellationToken cancellationToken)
        => Ok(await recursos.RecursosDoPlanoAsync(id, cancellationToken));

    [HttpPut("planos/{id:guid}/recursos")]
    public async Task<IActionResult> SalvarRecursosDoPlano(Guid id, [FromBody] SalvarRecursosPlanoRequest request, [FromServices] IAdministrarRecursosUseCase recursos, CancellationToken cancellationToken)
        => Ok(await recursos.SalvarRecursosDoPlanoAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpGet("tenants/{id:guid}/recursos")]
    public async Task<IActionResult> RecursosDaConta(Guid id, [FromServices] IAdministrarRecursosUseCase recursos, CancellationToken cancellationToken)
        => Ok(await recursos.RecursosDaContaAsync(id, cancellationToken));

    [HttpPost("tenants/{id:guid}/recursos/excecoes")]
    public async Task<IActionResult> SalvarExcecaoRecurso(Guid id, [FromBody] SalvarExcecaoRecursoRequest request, [FromServices] IAdministrarRecursosUseCase recursos, CancellationToken cancellationToken)
        => Ok(await recursos.SalvarExcecaoAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpDelete("tenants/{id:guid}/recursos/excecoes/{excecaoId:guid}")]
    public async Task<IActionResult> RemoverExcecaoRecurso(Guid id, Guid excecaoId, [FromServices] IAdministrarRecursosUseCase recursos, CancellationToken cancellationToken)
        => Ok(await recursos.RemoverExcecaoAsync(id, excecaoId, _currentUser.UserId, cancellationToken));

    // ---------- Planos (catálogo, preços com histórico, vagas de fundador) ----------

    [HttpGet("planos")]
    public async Task<IActionResult> ListarPlanos(CancellationToken cancellationToken)
        => Ok(await _planos.ListarAsync(cancellationToken));

    [HttpPost("planos")]
    public async Task<IActionResult> CriarPlano([FromBody] SalvarPlanoRequest request, CancellationToken cancellationToken)
        => Ok(await _planos.CriarAsync(request, _currentUser.UserId, cancellationToken));

    /// <summary>Altera nome, limites e oferta. Preço muda por POST planos/{id}/precos.</summary>
    [HttpPut("planos/{id:guid}")]
    public async Task<IActionResult> AlterarPlano(Guid id, [FromBody] SalvarPlanoRequest request, CancellationToken cancellationToken)
        => Ok(await _planos.AlterarAsync(id, request, _currentUser.UserId, cancellationToken));

    /// <summary>Excluir plano, passo 1: confere as regras e envia o código ao e-mail do operador.</summary>
    [HttpPost("planos/{id:guid}/exclusao/codigo")]
    public async Task<IActionResult> PedirCodigoExclusaoPlano(Guid id, CancellationToken cancellationToken)
        => Ok(await _solicitarExclusaoPlano.ExecutarAsync(id, OperadorId(), cancellationToken));

    /// <summary>Excluir plano, passo 2: com o código, apaga o plano e o histórico de preços dele.</summary>
    [HttpPost("planos/{id:guid}/exclusao/confirmar")]
    public async Task<IActionResult> ConfirmarExclusaoPlano(Guid id, [FromBody] ConfirmarExclusaoPlanoRequest request, CancellationToken cancellationToken)
    {
        await _confirmarExclusaoPlano.ExecutarAsync(id, OperadorId(), request.Codigo, cancellationToken);
        return NoContent();
    }

    [HttpGet("planos/{id:guid}/precos")]
    public async Task<IActionResult> HistoricoPrecos(Guid id, CancellationToken cancellationToken)
        => Ok(await _planos.HistoricoPrecosAsync(id, cancellationToken));

    /// <summary>Novo preço a partir de hoje ou de uma data futura (vale só para novas assinaturas).</summary>
    [HttpPost("planos/{id:guid}/precos")]
    public async Task<IActionResult> NovoPreco(Guid id, [FromBody] NovoPrecoPlanoRequest request, CancellationToken cancellationToken)
    {
        await _planos.AdicionarPrecoAsync(id, request, _currentUser.UserId, cancellationToken);
        return NoContent();
    }

    // ---------- Reajuste de quem já assina (B2) ----------

    /// <summary>Quem entra no reajuste para o preço de tabela do plano na data (e por que os outros ficam de fora).</summary>
    [HttpGet("planos/{id:guid}/reajuste/previa")]
    public async Task<IActionResult> PreviaReajuste(Guid id, [FromQuery] DateOnly aplicarEm, CancellationToken cancellationToken)
        => Ok(await _reajuste.PreviaAsync(id, aplicarEm, cancellationToken));

    /// <summary>Agenda o reajuste e manda o aviso por e-mail (pelo menos 30 dias antes).</summary>
    [HttpPost("planos/{id:guid}/reajuste")]
    public async Task<IActionResult> AgendarReajuste(Guid id, [FromBody] AgendarReajusteRequest request, CancellationToken cancellationToken)
        => Ok(await _reajuste.AgendarAsync(id, request, _currentUser.UserId, cancellationToken));

    [HttpGet("planos/{id:guid}/reajustes")]
    public async Task<IActionResult> ListarReajustes(Guid id, CancellationToken cancellationToken)
        => Ok(await _reajuste.ListarAsync(id, cancellationToken));

    /// <summary>Cancela o que ainda está agendado no lote (os já aplicados não mudam).</summary>
    [HttpPost("reajustes/lotes/{loteId:guid}/cancelar")]
    public async Task<IActionResult> CancelarLoteReajuste(Guid loteId, CancellationToken cancellationToken)
        => Ok(new { cancelados = await _reajuste.CancelarLoteAsync(loteId, _currentUser.UserId, cancellationToken) });

    [HttpGet("vagas-fundador")]
    public async Task<IActionResult> ListarVagasFundador(CancellationToken cancellationToken)
        => Ok(await _planos.ListarVagasAsync(cancellationToken));

    [HttpPut("vagas-fundador")]
    public async Task<IActionResult> AlterarVagasFundador([FromBody] AlterarVagasFundadorRequest request, CancellationToken cancellationToken)
    {
        await _planos.AlterarVagasAsync(request, _currentUser.UserId, cancellationToken);
        return NoContent();
    }
}
