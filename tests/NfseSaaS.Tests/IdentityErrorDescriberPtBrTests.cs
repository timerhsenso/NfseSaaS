using System.Reflection;
using Microsoft.AspNetCore.Identity;
using NfseSaaS.Infrastructure.Identity;
using Xunit;

namespace NfseSaaS.Tests;

public class IdentityErrorDescriberPtBrTests
{
    private static IEnumerable<MethodInfo> MetodosDoIdentity() =>
        typeof(IdentityErrorDescriber).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.IsVirtual && m.ReturnType == typeof(IdentityError));

    private static object?[] Argumentos(MethodInfo m) =>
        m.GetParameters().Select(p => p.ParameterType == typeof(int) ? (object)8 : "fulano@teste.com").ToArray();

    /// <summary>Uma versão nova do .NET que acrescente mensagem faz este teste falhar até ela ser traduzida.</summary>
    [Fact]
    public void Toda_mensagem_do_identity_foi_traduzida()
    {
        var naoTraduzidas = MetodosDoIdentity()
            .Where(m => typeof(IdentityErrorDescriberPtBr).GetMethod(m.Name, m.GetParameters().Select(p => p.ParameterType).ToArray())!.DeclaringType != typeof(IdentityErrorDescriberPtBr))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(naoTraduzidas);
    }

    [Fact]
    public void Codigos_sao_os_mesmos_do_identity_e_os_textos_mudam()
    {
        var original = new IdentityErrorDescriber();
        var ptBr = new IdentityErrorDescriberPtBr();

        foreach (var m in MetodosDoIdentity())
        {
            var esperado = (IdentityError)m.Invoke(original, Argumentos(m))!;
            var traduzido = (IdentityError)m.Invoke(ptBr, Argumentos(m))!;

            Assert.Equal(esperado.Code, traduzido.Code);
            Assert.NotEqual(esperado.Description, traduzido.Description);
            Assert.False(string.IsNullOrWhiteSpace(traduzido.Description));
        }
    }

    [Fact]
    public void Mensagens_de_senha_e_email_em_portugues()
    {
        var d = new IdentityErrorDescriberPtBr();
        Assert.Equal("A senha precisa ter pelo menos 8 caracteres.", d.PasswordTooShort(8).Description);
        Assert.Contains("símbolo", d.PasswordRequiresNonAlphanumeric().Description);
        Assert.Equal("O e-mail \"a@b.com\" já está em uso.", d.DuplicateUserName("a@b.com").Description);
        Assert.Contains("expirou", d.InvalidToken().Description);
    }
}
