using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace NfseSaaS.Web.Services;

/// <summary>
/// SEG1 — "confirme a sua senha" antes de operações sensíveis (trocar o
/// certificado, mudar permissões, levar a empresa para produção, exportar
/// os dados da conta…). Quem digitou a senha há pouco (login, cadastro,
/// convite, redefinição ou a própria confirmação) fica liberado por
/// <see cref="Minutos"/> minutos. Guardado num cookie HttpOnly assinado e
/// com validade pelo Data Protection, preso ao usuário: não serve para
/// outro login no mesmo navegador.
/// </summary>
public interface IConfirmacaoSenhaRecente
{
    void Marcar(HttpContext contexto, Guid usuarioId);

    bool EstaValida(HttpContext contexto, Guid usuarioId);

    void Limpar(HttpContext contexto);
}

public sealed class ConfirmacaoSenhaRecente : IConfirmacaoSenhaRecente
{
    public const string NomeCookie = "NfseSaaS.SenhaConfirmada";

    public const int Minutos = 15;

    public const string CodigoExigida = "confirmar_senha";

    public const string MensagemExigida = "Por segurança, confirme a sua senha para continuar.";

    private const string Finalidade = "NfseSaaS.ConfirmacaoSenha.v1";

    private readonly ITimeLimitedDataProtector _protetor;

    public ConfirmacaoSenhaRecente(IDataProtectionProvider protecao)
    {
        _protetor = protecao.CreateProtector(Finalidade).ToTimeLimitedDataProtector();
    }

    public void Marcar(HttpContext contexto, Guid usuarioId)
    {
        var valor = _protetor.Protect(usuarioId.ToString("N"), TimeSpan.FromMinutes(Minutos));
        contexto.Response.Cookies.Append(NomeCookie, valor, new CookieOptions
        {
            HttpOnly = true,
            Secure = contexto.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(Minutos)
        });
    }

    public bool EstaValida(HttpContext contexto, Guid usuarioId)
    {
        if (!contexto.Request.Cookies.TryGetValue(NomeCookie, out var valor) || string.IsNullOrEmpty(valor))
            return false;
        try
        {
            return _protetor.Unprotect(valor) == usuarioId.ToString("N");
        }
        catch (CryptographicException)
        {
            return false; // vencido, adulterado ou de outra instalação
        }
    }

    public void Limpar(HttpContext contexto) => contexto.Response.Cookies.Delete(NomeCookie, new CookieOptions { Path = "/" });
}
