using LogPilot.Application.Logs;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Authorize]
[Route("api/v1/logs")]
public sealed class LogsApiController : ControllerBase
{
    private readonly ILogQueryService _logs;

    public LogsApiController(ILogQueryService logs)
    {
        _logs = logs;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromBody] LogSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _logs.SearchAsync(userId, User.IsSystemAdmin(), request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(
        string id,
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _logs.GetDetailAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            id,
            startTime,
            endTime,
            cancellationToken);
        return Ok(detail);
    }

    [HttpGet("filters")]
    public async Task<IActionResult> Filters(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var filters = await _logs.GetFiltersAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            startTime,
            endTime,
            cancellationToken);
        return Ok(filters);
    }
}
