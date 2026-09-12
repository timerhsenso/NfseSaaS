using Microsoft.AspNetCore.Mvc;
using NfseSaaS.Application.Authorization;
using NfseSaaS.Application.UseCases.Certificados;
using NfseSaaS.Application.UseCases.Empresas;
using NfseSaaS.Application.UseCases.SincronizacaoSefin;
using NfseSaaS.Domain.Enums;
using NfseSaaS.Web.Filters;

namespace NfseSaaS.Web.Controllers.Api;

[ApiController]
[RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Consultar)]
[Route("api/empresas")]
public sealed class EmpresasController : ControllerBase
{
    private readonly ICadastrarEmpresaUseCase _cadastrarEmpresa;
    private readonly IListarEmpresasUseCase _listarEmpresas;
    private readonly IObterEmpresaPorIdUseCase _obterEmpresaPorId;
    private readonly IAtualizarEmpresaUseCase _atualizarEmpresa;
    private readonly IDesativarEmpresaUseCase _desativarEmpresa;
    private readonly IReativarEmpresaUseCase _reativarEmpresa;
    private readonly IExcluirEmpresaUseCase _excluirEmpresa;
    private readonly IAlterarAmbienteEmpresaUseCase _alterarAmbiente;
    private readonly IEnviarCertificadoUseCase _enviarCertificado;
    private readonly IObterStatusCertificadoUseCase _obterStatusCertificado;
    private readonly ITestarConexaoCertificadoUseCase _testarConexaoCertificado;
    private readonly ISincronizarNotasDaSefinUseCase _sincronizarNotasDaSefin;

    public EmpresasController(
        ICadastrarEmpresaUseCase cadastrarEmpresa,
        IListarEmpresasUseCase listarEmpresas,
        IObterEmpresaPorIdUseCase obterEmpresaPorId,
        IAtualizarEmpresaUseCase atualizarEmpresa,
        IDesativarEmpresaUseCase desativarEmpresa,
        IReativarEmpresaUseCase reativarEmpresa,
        IExcluirEmpresaUseCase excluirEmpresa,
        IAlterarAmbienteEmpresaUseCase alterarAmbiente,
        IEnviarCertificadoUseCase enviarCertificado,
        IObterStatusCertificadoUseCase obterStatusCertificado,
        ITestarConexaoCertificadoUseCase testarConexaoCertificado,
        ISincronizarNotasDaSefinUseCase sincronizarNotasDaSefin)
    {
        _cadastrarEmpresa = cadastrarEmpresa;
        _listarEmpresas = listarEmpresas;
        _obterEmpresaPorId = obterEmpresaPorId;
        _atualizarEmpresa = atualizarEmpresa;
        _desativarEmpresa = desativarEmpresa;
        _reativarEmpresa = reativarEmpresa;
        _excluirEmpresa = excluirEmpresa;
        _alterarAmbiente = alterarAmbiente;
        _enviarCertificado = enviarCertificado;
        _obterStatusCertificado = obterStatusCertificado;
        _testarConexaoCertificado = testarConexaoCertificado;
        _sincronizarNotasDaSefin = sincronizarNotasDaSefin;
    }

    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Incluir)]
    [HttpPost]
    public async Task<IActionResult> Cadastrar([FromBody] CadastrarEmpresaRequest request, CancellationToken cancellationToken)
    {
        var empresaId = await _cadastrarEmpresa.ExecutarAsync(request, cancellationToken);
        return Ok(new { empresaId });
    }

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? busca = null,
        [FromQuery] bool incluirInativas = false,
        CancellationToken cancellationToken = default)
    {
        var resultado = await _listarEmpresas.ExecutarAsync(
            new ListarEmpresasRequest(page, pageSize, busca, incluirInativas), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken cancellationToken)
    {
        var empresa = await _obterEmpresaPorId.ExecutarAsync(id, cancellationToken);
        return Ok(empresa);
    }

    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarEmpresaRequest request, CancellationToken cancellationToken)
    {
        await _atualizarEmpresa.ExecutarAsync(id, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Soft delete (Ativo=false) — reversível via /reativar. Use quando a Empresa já tem histórico.</summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken)
    {
        await _desativarEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken)
    {
        await _reativarEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Exclusão REAL — só funciona se a Empresa não tiver Cliente, Servico nem Nfse vinculados (422 caso contrário).</summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Excluir)]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        await _excluirEmpresa.ExecutarAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Troca o ambiente (Homologação ⇄ Produção) da Empresa — nos dois
    /// sentidos. A partir daqui, toda nova Nfse emitida por ela usa o
    /// ambiente novo; notas já emitidas mantêm o ambiente que tinham na
    /// hora (ver Nfse.TipoAmbiente). Idempotente: chamar de novo com o
    /// mesmo ambiente atual não faz nada (204 do mesmo jeito).
    /// </summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPut("{id:guid}/ambiente")]
    public async Task<IActionResult> AlterarAmbiente(Guid id, [FromBody] AlterarAmbienteEmpresaRequest request, CancellationToken cancellationToken)
    {
        await _alterarAmbiente.ExecutarAsync(id, request.TipoAmbiente, cancellationToken);
        return NoContent();
    }

    /// <summary>Status do certificado digital da Empresa (sem expor .pfx/senha) — Subject, validade, se está utilizável.</summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpGet("{id:guid}/certificado")]
    public async Task<IActionResult> ObterStatusCertificado(Guid id, CancellationToken cancellationToken)
    {
        var status = await _obterStatusCertificado.ExecutarAsync(id, cancellationToken);
        return Ok(status);
    }

    /// <summary>
    /// Cadastra/substitui o certificado digital (.pfx) da Empresa — mesmo
    /// processo que antes só era feito pela ferramenta de linha de
    /// comando NfseSaaS.CertTool. multipart/form-data: campo "arquivo"
    /// (o .pfx) + campo "senha".
    /// </summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/certificado")]
    [RequestSizeLimit(5 * 1024 * 1024)] // 5 MB é generoso pra um .pfx (tipicamente poucos KB)
    public async Task<IActionResult> EnviarCertificado(Guid id, IFormFile arquivo, [FromForm] string senha, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await arquivo.CopyToAsync(memoryStream, cancellationToken);

        var status = await _enviarCertificado.ExecutarAsync(id, memoryStream.ToArray(), senha, cancellationToken);
        return Ok(status);
    }

    /// <summary>Testa, de verdade, se o certificado cadastrado é aceito num handshake mTLS com a SEFIN Nacional — não emite nada.</summary>
    [RequerPermissao(TelaCatalogo.Empresas, AcaoPermissao.Alterar)]
    [HttpPost("{id:guid}/certificado/testar-conexao")]
    public async Task<IActionResult> TestarConexaoCertificado(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _testarConexaoCertificado.ExecutarAsync(id, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Puxa, via Distribuição de DF-e (ADN), notas emitidas por outro
    /// canal (portal web Emissor Nacional, outro sistema) que ainda não
    /// existem neste SaaS — ver SincronizarNotasDaSefinUseCase.
    /// </summary>
    [RequerPermissao(TelaCatalogo.Nfse, AcaoPermissao.Incluir)]
    [HttpPost("{id:guid}/sincronizar-sefin")]
    public async Task<IActionResult> SincronizarComSefin(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _sincronizarNotasDaSefin.ExecutarAsync(id, cancellationToken);
        return Ok(resultado);
    }
}
