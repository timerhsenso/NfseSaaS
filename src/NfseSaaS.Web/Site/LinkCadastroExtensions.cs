using Microsoft.AspNetCore.Http;
using NfseSaaS.Application.UseCases.Indicacao;

namespace NfseSaaS.Web.Site;

/// <summary>
/// Link do "Criar conta" nas páginas públicas, levando adiante o ?ref= (código
/// de indicação) e o ?origem= (canal) que trouxeram a pessoa ao site. Sem
/// cookie: o código viaja no endereço até o formulário de cadastro, que o
/// envia junto (ver registrar.js). Valores inválidos são descartados.
/// </summary>
public static class LinkCadastroExtensions
{
    public const string Cadastro = "/Account/Registrar";

    public static string LinkCadastro(this HttpContext contexto)
    {
        var codigo = CodigoIndicacao.Normalizar(contexto.Request.Query["ref"]);
        var origem = CodigoIndicacao.NormalizarOrigem(contexto.Request.Query["origem"]);

        var parametros = new List<string>();
        if (codigo is not null)
            parametros.Add($"ref={codigo}");
        if (origem is not null)
            parametros.Add($"origem={origem}");

        return parametros.Count == 0 ? Cadastro : $"{Cadastro}?{string.Join("&", parametros)}";
    }
}
