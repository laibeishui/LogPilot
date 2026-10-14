using System.Text.Json;
using LogPilot.Application.Common;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;

namespace LogPilot.Application.Alerting;

public interface IAlertEventService
{
    Task<AlertEventListResponse> ListAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        AlertEventStatus? status,
        int limit,
        long? cursor,
        CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(long userId, bool isSystemAdmin, long projectId, long eventId, CancellationToken cancellationToken = default);

    Task ResolveAsync(long userId, bool isSystemAdmin, long projectId, long eventId, CancellationToken cancellationToken = default);
}

public sealed class AlertEventService : IAlertEventService
{
    private readonly IAlertEventRepository _events;
    private readonly IProjectAccessService _access;

    public AlertEventService(IAlertEventRepository events, IProjectAccessService access)
    {
        _events = events;
        _access = access;
    }

    public async Task<AlertEventListResponse> ListAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        AlertEventStatus? status,
        int limit,
        long? cursor,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);

        limit = Math.Clamp(limit, 1, 100);
        var items = await _events.ListByProjectAsync(projectId, status, limit + 1, cursor, cancellationToken).ConfigureAwait(false);

        var hasMore = items.Count > limit;
        var resultItems = hasMore ? items.Take(limit).ToList() : items;
        long? nextCursor = hasMore ? resultItems[^1].Id : null;

        return new AlertEventListResponse
        {
            Items = resultItems.Select(Map).ToList(),
            NextCursor = nextCursor,
            HasMore = hasMore
        };
    }

    public async Task AcknowledgeAsync(long userId, bool isSystemAdmin, long projectId, long eventId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var events = await _events.ListByProjectAsync(projectId, null, 1, eventId + 1, cancellationToken).ConfigureAwait(false);
        if (events.All(e => e.Id != eventId))
        {
            throw new AppException("NOT_FOUND", "告警事件不存在。", 404);
        }

        await _events.UpdateStatusAsync(eventId, AlertEventStatus.Acknowledged, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task ResolveAsync(long userId, bool isSystemAdmin, long projectId, long eventId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var events = await _events.ListByProjectAsync(projectId, null, 1, eventId + 1, cancellationToken).ConfigureAwait(false);
        if (events.All(e => e.Id != eventId))
        {
            throw new AppException("NOT_FOUND", "告警事件不存在。", 404);
        }

        await _events.UpdateStatusAsync(eventId, AlertEventStatus.Resolved, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    private static AlertEventDto Map(Core.Models.AlertEvent e) => new()
    {
        Id = e.Id,
        RuleId = e.RuleId,
        ProjectId = e.ProjectId,
        Fingerprint = e.Fingerprint,
        Status = e.Status.ToString().ToLowerInvariant(),
        Severity = e.Severity.ToString(),
        Title = e.Title,
        Message = e.Message,
        SampleLogs = ParseSampleLogs(e.SampleLogsJson),
        TriggeredAt = e.TriggeredAt,
        AcknowledgedAt = e.AcknowledgedAt,
        ResolvedAt = e.ResolvedAt,
        NotifyCount = e.NotifyCount
    };

    private static IReadOnlyList<SampleLogDto> ParseSampleLogs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SampleLogDto>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SampleLogDto>>(json) ?? new List<SampleLogDto>();
        }
        catch
        {
            return Array.Empty<SampleLogDto>();
        }
    }
}
