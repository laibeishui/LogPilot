using LogPilot.Application.Alerting;
using LogPilot.Core.Enums;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Authorize]
[Route("api/v1/alerts")]
public sealed class AlertsApiController : ControllerBase
{
    private readonly IAlertChannelService _channels;
    private readonly IAlertRuleService _rules;
    private readonly IAlertEventService _events;

    public AlertsApiController(
        IAlertChannelService channels,
        IAlertRuleService rules,
        IAlertEventService events)
    {
        _channels = channels;
        _rules = rules;
        _events = events;
    }

    // —— 通知渠道 ——

    [HttpGet("projects/{projectId:long}/channels")]
    public async Task<IActionResult> ListChannels(long projectId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var items = await _channels.ListAsync(userId, User.IsSystemAdmin(), projectId, cancellationToken);
        return Ok(items);
    }

    [HttpPost("projects/{projectId:long}/channels")]
    public async Task<IActionResult> CreateChannel(
        long projectId,
        [FromBody] CreateAlertChannelRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var channel = await _channels.CreateAsync(userId, User.IsSystemAdmin(), projectId, request, cancellationToken);
        return Ok(channel);
    }

    [HttpPatch("projects/{projectId:long}/channels/{channelId:long}")]
    public async Task<IActionResult> UpdateChannel(
        long projectId,
        long channelId,
        [FromBody] UpdateAlertChannelRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var channel = await _channels.UpdateAsync(userId, User.IsSystemAdmin(), projectId, channelId, request, cancellationToken);
        return Ok(channel);
    }

    [HttpDelete("projects/{projectId:long}/channels/{channelId:long}")]
    public async Task<IActionResult> DeleteChannel(long projectId, long channelId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _channels.DeleteAsync(userId, User.IsSystemAdmin(), projectId, channelId, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("projects/{projectId:long}/channels/{channelId:long}/test")]
    public async Task<IActionResult> TestChannel(long projectId, long channelId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _channels.TestAsync(userId, User.IsSystemAdmin(), projectId, channelId, cancellationToken);
        return Ok(new { ok = true });
    }

    // —— 告警规则 ——

    [HttpGet("projects/{projectId:long}/rules")]
    public async Task<IActionResult> ListRules(long projectId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var items = await _rules.ListAsync(userId, User.IsSystemAdmin(), projectId, cancellationToken);
        return Ok(items);
    }

    [HttpPost("projects/{projectId:long}/rules")]
    public async Task<IActionResult> CreateRule(
        long projectId,
        [FromBody] CreateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var rule = await _rules.CreateAsync(userId, User.IsSystemAdmin(), projectId, request, cancellationToken);
        return Ok(rule);
    }

    [HttpGet("projects/{projectId:long}/rules/{ruleId:long}")]
    public async Task<IActionResult> GetRule(long projectId, long ruleId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var rule = await _rules.GetAsync(userId, User.IsSystemAdmin(), projectId, ruleId, cancellationToken);
        return Ok(rule);
    }

    [HttpPatch("projects/{projectId:long}/rules/{ruleId:long}")]
    public async Task<IActionResult> UpdateRule(
        long projectId,
        long ruleId,
        [FromBody] UpdateAlertRuleRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var rule = await _rules.UpdateAsync(userId, User.IsSystemAdmin(), projectId, ruleId, request, cancellationToken);
        return Ok(rule);
    }

    [HttpDelete("projects/{projectId:long}/rules/{ruleId:long}")]
    public async Task<IActionResult> DeleteRule(long projectId, long ruleId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _rules.DeleteAsync(userId, User.IsSystemAdmin(), projectId, ruleId, cancellationToken);
        return Ok(new { ok = true });
    }

    // —— 告警事件 ——

    [HttpGet("projects/{projectId:long}/events")]
    public async Task<IActionResult> ListEvents(
        long projectId,
        [FromQuery] string? status,
        [FromQuery] int limit = 20,
        [FromQuery] long? cursor = null,
        CancellationToken cancellationToken = default)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        AlertEventStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            statusFilter = status.ToLowerInvariant() switch
            {
                "firing" => AlertEventStatus.Firing,
                "acknowledged" => AlertEventStatus.Acknowledged,
                "resolved" => AlertEventStatus.Resolved,
                "silenced" => AlertEventStatus.Silenced,
                _ => null
            };
        }

        var result = await _events.ListAsync(userId, User.IsSystemAdmin(), projectId, statusFilter, limit, cursor, cancellationToken);
        return Ok(result);
    }

    [HttpPost("projects/{projectId:long}/events/{eventId:long}/acknowledge")]
    public async Task<IActionResult> AcknowledgeEvent(long projectId, long eventId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _events.AcknowledgeAsync(userId, User.IsSystemAdmin(), projectId, eventId, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpPost("projects/{projectId:long}/events/{eventId:long}/resolve")]
    public async Task<IActionResult> ResolveEvent(long projectId, long eventId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _events.ResolveAsync(userId, User.IsSystemAdmin(), projectId, eventId, cancellationToken);
        return Ok(new { ok = true });
    }
}
