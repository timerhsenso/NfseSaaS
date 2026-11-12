using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

/// <summary>
/// "Configurações da conta" (menu do usuário, administrador): o que vale
/// para a conta inteira, uma seção por assunto — hoje a Logomarca (B9).
/// As cores do sistema ficam no painel Aparência. Dados
/// via api/aparencia; quem não é administrador vê a tela só para leitura
/// (o servidor confere de novo em cada gravação).
/// </summary>
[Authorize]
public sealed class ConfiguracoesContaController : Controller
{
    public IActionResult Index() => View();
}
