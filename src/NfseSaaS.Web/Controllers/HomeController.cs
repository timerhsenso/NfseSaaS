using Microsoft.AspNetCore.Mvc;

namespace NfseSaaS.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
