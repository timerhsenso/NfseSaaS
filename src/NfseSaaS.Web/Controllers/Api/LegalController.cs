using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Application.Common;
using NfseSaaS.Application.Legal;
using NfseSaaS.Domain.Entities;
using NfseSaaS.Domain.Legal;
using NfseSaaS.Infrastructure.Persistence;
using NfseSaaS.Web.Filters;
using NfseSaaS.Web.Services;

namespace NfseSaaS.Web.Controllers.Api;

/// <param name="Aceito">Termos de uso e Política de privacidade (obrigatório quando a versão atual ainda não foi aceita).</param>
/// <param name="AceitoDpa">Acordo de Tratamento de Dados (administrador; obrigatório junto com a versão nova ou depois da vigência).</param>
/// <param name="Certificados">Empresas cuja Autorização do certificado A1 o administrador aceita agora.</param>
/// <param name="EmissaoAutomatica">Empresas cuja Autorização de emissão automática o administrador aceita agora (pede a senha).</param>
public sealed record AceitarTermosRequest(
    bool Aceito,
    bool AceitoDpa = false,
    IReadOnlyList<Guid>? Certificados = null,
    IReadOnlyList<Guid>? EmissaoAutomatica = null);

/// <summary>
/// Aceite das versões dos Termos de uso e da Política de privacidade pelo
/// usuário logado (tela /Legal/Aceitar) e, para o administrador, do DPA e das
/// autorizações (JUR1). Cadastro e convite gravam o aceite direto em
/// AuthController. "Documentos aceitos" de Configurações da conta também vêm daqui.
/// </summary>
[ApiController]
[Route("api/legal")]
[Authorize]
[PermitirSemAceiteTermos]
public sealed class LegalController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAceiteTermosService _aceiteTermos;
    private readonly IAceiteInstrumentosService _instrumentos;
    private readonly IConfirmacaoSenhaRecente _confirmacaoSenha;
    private readonly ICurrentUser _currentUser;

    public LegalController(
        AppDbContext db,
        IAceiteTermosService aceiteTermos,
        IAceiteInstrumentosService instrumentos,
        IConfirmacaoSenhaRecente confirmacaoSenha,
        ICurrentUser currentUser)
    {
        _db = db;
        _aceiteTermos = aceiteTermos;
        _instrumentos = instrumentos;
        _confirmacaoSenha = confirmacaoSenha;
        _currentUser = currentUser;
    }

    /// <summary>Versão atual, se o usuário logado já aceitou e o que falta ao administrador.</summary>
    [HttpGet("situacao")]
    public async Task<IActionResult> Situacao(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();

        var hoje = DataBrasilia.Hoje();
        var atual = DocumentosLegais.Atual(hoje);
        return Ok(new
        {
            versaoTermosDeUso = atual.Versao,
            versaoPoliticaPrivacidade = atual.Versao,
            vigentesDesde = atual.VigenteDesde,
            emAvisoPrevio = DocumentosLegais.EmAvisoPrevio(hoje),
            aceito = await _aceiteTermos.AceitouVersaoVigenteAsync(usuarioId, cancellationToken),
            aceitoVersaoAtual = await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken),
            pendencias = await _instrumentos.PendenciasAsync(usuarioId, cancellationToken)
        });
    }

    /// <summary>
    /// Grava os aceites marcados (versão, data, IP, navegador e, nos
    /// instrumentos, o hash do texto). Repetir é inofensivo: o que já foi
    /// aceito não é gravado de novo.
    /// </summary>
    [HttpPost("aceitar")]
    public async Task<IActionResult> Aceitar([FromBody] AceitarTermosRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();

        var termosPendentes = !await _aceiteTermos.AceitouVersaoAtualAsync(usuarioId, cancellationToken);
        if (termosPendentes && !request.Aceito)
            return BadRequest(new { erro = "Marque a caixa confirmando que leu e aceita os Termos de uso e a Política de privacidade." });

        var pendencias = await _instrumentos.PendenciasAsync(usuarioId, cancellationToken);

        // O DPA integra os Termos (item 15): o administrador aceita junto com a versão nova, ou quando passa a valer.
        var dpaExigido = pendencias.DpaPendente && (termosPendentes || pendencias.DpaObrigatorio);
        if (dpaExigido && !request.AceitoDpa)
            return BadRequest(new { erro = "Marque a caixa do Acordo de Tratamento de Dados Pessoais (DPA): como administrador, você aceita em nome da conta." });

        var certificados = (request.Certificados ?? []).Distinct().ToList();
        var automaticas = (request.EmissaoAutomatica ?? []).Distinct().ToList();
        if (certificados.Any(id => pendencias.Certificados.All(e => e.EmpresaId != id))
            || automaticas.Any(id => pendencias.EmissaoAutomatica.All(e => e.EmpresaId != id)))
            return UnprocessableEntity(new { erro = "Autorização de empresa que não está pendente nesta conta. Recarregue a tela." });

        // Autorização de emissão automática, item 6: depois de confirmar a senha.
        if (automaticas.Count > 0 && !_confirmacaoSenha.EstaValida(HttpContext, usuarioId))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                erro = ConfirmacaoSenhaRecente.MensagemExigida,
                codigo = ConfirmacaoSenhaRecente.CodigoExigida
            });

        if (termosPendentes)
            _aceiteTermos.Registrar(usuarioId, OrigemAceiteTermos.Atualizacao);
        if (pendencias.DpaPendente && request.AceitoDpa)
            await _instrumentos.RegistrarDpaAsync(usuarioId, OrigemAceiteInstrumento.Atualizacao, cancellationToken);
        foreach (var empresaId in certificados)
            await _instrumentos.RegistrarAutorizacaoCertificadoAsync(usuarioId, empresaId, OrigemAceiteInstrumento.Atualizacao, cancellationToken);
        foreach (var empresaId in automaticas)
            await _instrumentos.RegistrarAutorizacaoEmissaoAutomaticaAsync(usuarioId, empresaId, OrigemAceiteInstrumento.Atualizacao, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        if (termosPendentes)
            _aceiteTermos.MarcarComoAceito(usuarioId);

        return NoContent();
    }

    /// <summary>Documentos aceitos (administrador: a conta toda; os demais: os próprios).</summary>
    [HttpGet("aceitos")]
    public async Task<IActionResult> Aceitos(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();
        return Ok(await _instrumentos.ListarAsync(usuarioId, cancellationToken));
    }

    /// <summary>Comprovante em PDF de um aceite (origem "termos" ou "instrumento").</summary>
    [HttpGet("aceitos/{origem}/{id:guid}/comprovante")]
    public async Task<IActionResult> Comprovante(string origem, Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } usuarioId)
            return Unauthorized();
        var arquivo = await _instrumentos.ComprovanteAsync(usuarioId, origem, id, cancellationToken);
        return arquivo is null ? NotFound() : File(arquivo.Conteudo, arquivo.TipoConteudo, arquivo.NomeArquivo);
    }
}
