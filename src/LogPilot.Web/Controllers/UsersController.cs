using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 人员管理页面（仅系统管理员）。
/// </summary>
[Authorize(Policy = LogPilotPolicies.SystemAdmin)]
[Route("users")]
public sealed class UsersController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
