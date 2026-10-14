using LogPilot.Application.Common;
using LogPilot.Application.Options;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using Microsoft.Extensions.Options;

namespace LogPilot.Application.Logs;

/// <summary>
/// 日志查询应用服务：强制项目权限与查询窗口，禁止透传任意 LogsQL。
/// </summary>
public sealed class LogQueryService : ILogQueryService
{
    private static readonly string[] DefaultLevels =
    [
        "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"
    ];

    private readonly ILogSearcher _searcher;
    private readonly IProjectAccessService _access;
    private readonly QueryLimits _limits;

    public LogQueryService(
        ILogSearcher searcher,
        IProjectAccessService access,
        IOptions<QueryLimits> limits)
    {
        _searcher = searcher;
        _access = access;
        _limits = limits.Value;
    }

    public async Task<LogSearchResponseDto> SearchAsync(
        long userId,
        bool isSystemAdmin,
        LogSearchRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProjectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, request.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(request.StartTime, request.EndTime);
        var pageSize = Math.Clamp(
            request.PageSize ?? _limits.DefaultPageSize,
            1,
            _limits.MaxPageSize);

        var result = await _searcher.SearchAsync(
            new AuthorizedLogQuery
            {
                ProjectId = request.ProjectId,
                StartTime = start,
                EndTime = end,
                Services = NormalizeList(request.Services),
                Levels = NormalizeList(request.Levels),
                Keyword = TrimToNull(request.Keyword),
                TraceId = TrimToNull(request.TraceId),
                PageSize = pageSize,
                Cursor = TrimToNull(request.Cursor)
            },
            cancellationToken).ConfigureAwait(false);

        return new LogSearchResponseDto
        {
            Items = result.Items.Select(MapItem).ToList(),
            NextCursor = result.NextCursor,
            HasMore = result.HasMore
        };
    }

    public async Task<LogDetailDto> GetDetailAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        string eventId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new AppException("INVALID_ARGUMENT", "日志 id 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(startTime, endTime);
        var detail = await _searcher.GetByEventIdAsync(
            projectId,
            eventId.Trim(),
            start,
            end,
            cancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            throw new AppException("NOT_FOUND", "日志不存在或已过期。", 404);
        }

        return MapDetail(detail);
    }

    public async Task<LogFiltersDto> GetFiltersAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(startTime, endTime);
        var services = await _searcher.ListServicesAsync(projectId, start, end, cancellationToken)
            .ConfigureAwait(false);

        return new LogFiltersDto
        {
            Services = services,
            Levels = DefaultLevels
        };
    }

    public async Task<StatsOverviewDto> GetOverviewAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(startTime, endTime);

        var totalTask = _searcher.CountAsync(
            projectId, start, end, null, Array.Empty<string>(), null, null, cancellationToken);

        var errorTask = _searcher.CountAsync(
            projectId, start, end, null, new[] { "ERROR", "FATAL" }, null, null, cancellationToken);

        var servicesTask = _searcher.ListServicesAsync(projectId, start, end, cancellationToken);

        await Task.WhenAll(totalTask, errorTask, servicesTask).ConfigureAwait(false);

        return new StatsOverviewDto
        {
            ProjectId = projectId,
            TotalLogs = totalTask.Result,
            ErrorLogs = errorTask.Result,
            ServiceCount = servicesTask.Result.Count,
            StartTime = start,
            EndTime = end
        };
    }

    public async Task<StatsTrendDto> GetTrendAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        string? interval,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(startTime, endTime);
        var cleanInterval = NormalizeInterval(interval);

        var allTask = _searcher.TrendAsync(projectId, start, end, cleanInterval, false, cancellationToken);
        var errTask = _searcher.TrendAsync(projectId, start, end, cleanInterval, true, cancellationToken);

        await Task.WhenAll(allTask, errTask).ConfigureAwait(false);

        var errorByBucket = errTask.Result.ToDictionary(p => p.Bucket, p => p.Count);

        var points = allTask.Result
            .Select(p => new TrendPointDto
            {
                Time = p.Bucket,
                Count = p.Count,
                Errors = errorByBucket.TryGetValue(p.Bucket, out var e) ? e : 0
            })
            .ToList();

        return new StatsTrendDto
        {
            Interval = cleanInterval,
            Points = points
        };
    }

    private static string NormalizeInterval(string? interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            return "5m";
        }

        var trimmed = interval.Trim().ToLowerInvariant();
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+[smhd]$"))
        {
            return trimmed;
        }

        return "5m";
    }

    private (DateTimeOffset Start, DateTimeOffset End) ResolveRange(
        DateTimeOffset? startTime,
        DateTimeOffset? endTime)
    {
        var end = (endTime ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var start = (startTime ?? end.AddMinutes(-_limits.DefaultRangeMinutes)).ToUniversalTime();

        if (start >= end)
        {
            throw new AppException("INVALID_ARGUMENT", "startTime 必须早于 endTime。");
        }

        var maxRange = TimeSpan.FromHours(Math.Max(1, _limits.MaxRangeHours));
        if (end - start > maxRange)
        {
            throw new AppException(
                "INVALID_ARGUMENT",
                $"查询时间窗不能超过 {_limits.MaxRangeHours} 小时。");
        }

        return (start, end);
    }

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        return values
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(50)
            .ToList();
    }

    private static string? TrimToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static LogSearchItemDto MapItem(LogSearchItem item)
        => new()
        {
            Id = item.Id,
            Timestamp = item.Timestamp,
            ServiceName = item.ServiceName,
            Level = item.Level,
            Message = item.Message,
            TraceId = item.TraceId
        };

    private static LogDetailDto MapDetail(LogDetail detail)
        => new()
        {
            Id = detail.Id,
            ProjectId = detail.ProjectId,
            Timestamp = detail.Timestamp,
            ReceivedAt = detail.ReceivedAt,
            ServiceName = detail.ServiceName,
            Environment = detail.Environment,
            Level = detail.Level,
            Message = detail.Message,
            TraceId = detail.TraceId,
            SpanId = detail.SpanId,
            HostName = detail.HostName,
            ExceptionType = detail.ExceptionType,
            ExceptionMessage = detail.ExceptionMessage,
            ExceptionStack = detail.ExceptionStack,
            Attributes = detail.Attributes
        };
}
