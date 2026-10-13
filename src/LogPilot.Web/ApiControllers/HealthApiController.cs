using LogPilot.Application.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

/// <summary>
/// 健康检查：/health/live 与 /health/ready。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("health")]
public sealed class HealthApiController : ControllerBase
{
    private readonly ISystemHealthService _healthService;

    public HealthApiController(ISystemHealthService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet("live")]
    public async Task<IActionResult> Live(CancellationToken cancellationToken)
    {
        var report = await _healthService.GetLiveAsync(cancellationToken);
        return Ok(report);
    }

    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var report = await _healthService.GetReadyAsync(cancellationToken);
        return report.IsReady ? Ok(report) : StatusCode(StatusCodes.Status503ServiceUnavailable, report);
    }
}
