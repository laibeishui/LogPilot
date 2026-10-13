using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.Controllers;

/// <summary>
/// 项目管理页面。
/// </summary>
[Route("projects")]
public sealed class ProjectsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
