using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 日志检索页面（Razor + Vue 渐进增强，M4 接真实 API）。
/// </summary>
[Route("logs")]
public sealed class LogsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
