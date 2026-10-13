using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 系统设置 / 基础诊断页面。
/// </summary>
[Route("settings")]
public sealed class SettingsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
