using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 告警中心：告警事件、规则、通知渠道管理。
/// </summary>
[Route("alerts")]
public sealed class AlertsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
