using LogPilot.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 系统设置 / 基础诊断页面。
/// </summary>
[Route("settings")]
public sealed class SettingsController : Controller
{
    private readonly ILogWriteMetrics _writeMetrics;

    public SettingsController(ILogWriteMetrics writeMetrics)
    {
        _writeMetrics = writeMetrics;
    }

    [HttpGet("")]
    public IActionResult Index() => View(_writeMetrics.GetSnapshot());
}
