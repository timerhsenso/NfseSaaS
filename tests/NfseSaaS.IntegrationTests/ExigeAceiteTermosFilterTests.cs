using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using NfseSaaS.Application.Abstractions;
using NfseSaaS.Web.Filters;
using Xunit;

namespace NfseSaaS.IntegrationTests;

/// <summary>
/// Regras do ExigeAceiteTermosFilter, sem banco: quem passa direto, quem é
/// barrado e como (redirecionamento na tela, 403 "termos_pendentes" na API).
/// </summary>
public class ExigeAceiteTermosFilterTests
{
    private static readonly Guid UsuarioId = Guid.NewGuid();

    [Fact]
    public async Task Anonimo_passa_direto_sem_consultar_aceite()
    {
        var aceite = new AceiteFalso(aceitou: false);
        var (contexto, executou) = await ExecutarAsync(aceite, autenticado: false, caminho: "/Clientes");

        Assert.True(executou);
        Assert.Null(contexto.Result);
        Assert.Equal(0, aceite.Consultas);
    }

    [Fact]
    public async Task Usuario_que_aceitou_a_versao_vigente_passa()
    {
        var (contexto, executou) = await ExecutarAsync(new AceiteFalso(aceitou: true), autenticado: true, caminho: "/Clientes");

        Assert.True(executou);
        Assert.Null(contexto.Result);
    }

    [Fact]
    public async Task Tela_sem_aceite_redireciona_para_o_aceite_com_retorno()
    {
        var (contexto, executou) = await ExecutarAsync(new AceiteFalso(aceitou: false), autenticado: true, caminho: "/Clientes", query: "?x=1");

        Assert.False(executou);
        var redirecionamento = Assert.IsType<RedirectResult>(contexto.Result);
        Assert.Equal("/Legal/Aceitar?returnUrl=%2FClientes%3Fx%3D1", redirecionamento.Url);
    }

    [Fact]
    public async Task Raiz_sem_aceite_redireciona_sem_returnUrl()
    {
        var (contexto, _) = await ExecutarAsync(new AceiteFalso(aceitou: false), autenticado: true, caminho: "/");

        var redirecionamento = Assert.IsType<RedirectResult>(contexto.Result);
        Assert.Equal("/Legal/Aceitar", redirecionamento.Url);
    }

    [Fact]
    public async Task Api_sem_aceite_devolve_403_com_codigo_termos_pendentes()
    {
        var (contexto, executou) = await ExecutarAsync(
            new AceiteFalso(aceitou: false), autenticado: true, caminho: "/api/clientes", metadados: new object[] { new ApiControllerAttribute() });

        Assert.False(executou);
        var resultado = Assert.IsType<ObjectResult>(contexto.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, resultado.StatusCode);

        var codigo = resultado.Value!.GetType().GetProperty("codigo")!.GetValue(resultado.Value);
        Assert.Equal(ExigeAceiteTermosFilter.CodigoTermosPendentes, codigo);
    }

    [Fact]
    public async Task Rota_liberada_passa_mesmo_sem_aceite()
    {
        var aceite = new AceiteFalso(aceitou: false);
        var (contexto, executou) = await ExecutarAsync(
            aceite, autenticado: true, caminho: "/api/auth/logout", metadados: new object[] { new PermitirSemAceiteTermosAttribute() });

        Assert.True(executou);
        Assert.Null(contexto.Result);
        Assert.Equal(0, aceite.Consultas);
    }

    [Fact]
    public void Post_fora_da_api_volta_para_a_raiz_depois_do_aceite()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Post;
        http.Request.Path = "/Clientes/Salvar";

        Assert.Equal("/Legal/Aceitar", ExigeAceiteTermosFilter.MontarUrlTelaAceite(http.Request));
    }

    private static async Task<(ActionExecutingContext Contexto, bool Executou)> ExecutarAsync(
        AceiteFalso aceite, bool autenticado, string caminho, string query = "", object[]? metadados = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = caminho;
        http.Request.QueryString = new QueryString(string.IsNullOrEmpty(query) ? null : query);
        http.User = autenticado
            ? new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UsuarioId.ToString()) }, "teste"))
            : new ClaimsPrincipal(new ClaimsIdentity());

        var descritor = new ActionDescriptor { EndpointMetadata = metadados?.ToList() ?? new List<object>() };
        var actionContext = new ActionContext(http, new RouteData(), descritor);
        var contexto = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: new object());

        var executou = false;
        var filtro = new ExigeAceiteTermosFilter(aceite, new UsuarioFalso(autenticado ? UsuarioId : null));

        await filtro.OnActionExecutionAsync(contexto, () =>
        {
            executou = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });

        return (contexto, executou);
    }

    private sealed class AceiteFalso : IAceiteTermosService
    {
        private readonly bool _aceitou;

        public AceiteFalso(bool aceitou) => _aceitou = aceitou;

        public int Consultas { get; private set; }

        public Task<bool> AceitouVersaoVigenteAsync(Guid usuarioId, CancellationToken cancellationToken)
        {
            Consultas++;
            return Task.FromResult(_aceitou);
        }

        public void Registrar(Guid usuarioId, string origem) => throw new NotSupportedException();

        public void MarcarComoAceito(Guid usuarioId) => throw new NotSupportedException();
    }

    private sealed class UsuarioFalso : ICurrentUser
    {
        public UsuarioFalso(Guid? userId) => UserId = userId;

        public Guid? UserId { get; }

        public string? IpAddress => "127.0.0.1";
    }
}
