using LogPilot.Application.Logs;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

/// <summary>
/// 日志统计 API：概览与趋势，用于总览页。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/stats")]
public sealed class StatsApiController : ControllerBase
{
    private readonly ILogQueryService _logs;

    public StatsApiController(ILogQueryService logs)
    {
        _logs = logs;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var overview = await _logs.GetOverviewAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            startTime,
            endTime,
            cancellationToken);

        return Ok(overview);
    }

    [HttpGet("trend")]
    public async Task<IActionResult> Trend(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        [FromQuery] string? interval,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var trend = await _logs.GetTrendAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            startTime,
            endTime,
            interval,
            cancellationToken);

        return Ok(trend);
    }

    /// <summary>
    /// 异常聚合：把时间窗内的错误日志按指纹归并为"问题"列表。
    /// </summary>
    [HttpGet("exceptions")]
    public async Task<IActionResult> Exceptions(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        [FromQuery] int? top,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _logs.GetExceptionsAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            startTime,
            endTime,
            top,
            cancellationToken);

        return Ok(result);
    }
}
