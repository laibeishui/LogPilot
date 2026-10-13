using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 首次管理员初始化页面。
/// </summary>
[AllowAnonymous]
[Route("setup")]
public sealed class SetupController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
