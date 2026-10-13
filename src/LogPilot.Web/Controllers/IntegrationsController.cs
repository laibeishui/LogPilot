using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 接入中心页面。
/// </summary>
[Route("integrations")]
public sealed class IntegrationsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
