using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 告警中心占位页（V0.3，不可执行伪操作）。
/// </summary>
[Route("alerts")]
public sealed class AlertsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
