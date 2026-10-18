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

    /// <summary>异常聚合时参与指纹计算的错误样本上限。</summary>
    private const int ErrorSampleLimit = 1000;

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
        var page = Math.Max(1, request.Page ?? 1);
        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);

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
                Offset = offset
            },
            cancellationToken).ConfigureAwait(false);

        return new LogSearchResponseDto
        {
            Items = result.Items.Select(MapItem).ToList(),
            Total = result.Total,
            Page = page,
            PageSize = pageSize
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

    public async Task<LogContextDto> GetContextAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? timestamp,
        int? beforeMinutes,
        int? afterMinutes,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var anchor = (timestamp ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var before = Math.Clamp(beforeMinutes ?? 5, 1, 120);
        var after = Math.Clamp(afterMinutes ?? 5, 1, 120);
        var take = Math.Clamp(limit ?? 20, 1, 50);

        var beforeTask = _searcher.SearchAsync(
            new AuthorizedLogQuery
            {
                ProjectId = projectId,
                StartTime = anchor.AddMinutes(-before),
                EndTime = anchor,
                PageSize = take
            },
            cancellationToken);

        var afterTask = _searcher.SearchAsync(
            new AuthorizedLogQuery
            {
                ProjectId = projectId,
                StartTime = anchor,
                EndTime = anchor.AddMinutes(after),
                PageSize = take
            },
            cancellationToken);

        await Task.WhenAll(beforeTask, afterTask).ConfigureAwait(false);

        var items = beforeTask.Result.Items
            .Concat(afterTask.Result.Items)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(x => x.Timestamp)
            .Select(MapItem)
            .ToList();

        return new LogContextDto { Items = items };
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
            projectId, start, end, null, null, Array.Empty<string>(), null, null, cancellationToken);

        var errorTask = _searcher.CountAsync(
            projectId, start, end, null, null, new[] { "ERROR", "FATAL" }, null, null, cancellationToken);

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

    public async Task<StatsExceptionsDto> GetExceptionsAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        int? top,
        CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var (start, end) = ResolveRange(startTime, endTime);
        var take = Math.Clamp(top ?? 20, 1, 100);
        var samples = await _searcher
            .ListErrorSamplesAsync(projectId, start, end, ErrorSampleLimit, cancellationToken)
            .ConfigureAwait(false);

        var groups = samples
            .GroupBy(s => ExceptionFingerprint.Compute(s.ServiceName, s.Level, s.ExceptionType, s.Message))
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Timestamp).First();
                return new ExceptionGroupDto
                {
                    Fingerprint = g.Key,
                    Title = BuildTitle(latest),
                    ServiceName = latest.ServiceName,
                    Level = latest.Level,
                    Count = g.LongCount(),
                    FirstSeen = g.Min(x => x.Timestamp),
                    LastSeen = g.Max(x => x.Timestamp),
                    SampleMessage = latest.Message,
                    SampleTraceId = latest.TraceId,
                    ExceptionType = latest.ExceptionType
                };
            })
            .OrderByDescending(x => x.Count)
            .ThenByDescending(x => x.LastSeen)
            .Take(take)
            .ToList();

        return new StatsExceptionsDto
        {
            StartTime = start,
            EndTime = end,
            Scanned = samples.Count,
            Truncated = samples.Count >= ErrorSampleLimit,
            Groups = groups
        };
    }

    public async IAsyncEnumerable<LogSearchItemDto> TailAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        string? service,
        string? level,
        string? keyword,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (projectId <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "projectId 必填。");
        }

        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var services = NormalizeList(service is null ? null : new[] { service });
        var levels = NormalizeList(level is null ? null : new[] { level });
        var keywordFilter = TrimToNull(keyword);

        // 长连接期间周期性复核权限：成员被移出项目后，已建立的连接必须在有限时间内失效。
        var recheckInterval = TimeSpan.FromSeconds(Math.Max(1, _limits.StreamRecheckSeconds));
        var nextRecheck = DateTimeOffset.UtcNow.Add(recheckInterval);

        await foreach (var item in _searcher
            .TailAsync(projectId, services, levels, keywordFilter, cancellationToken)
            .ConfigureAwait(false))
        {
            var now = DateTimeOffset.UtcNow;
            if (now >= nextRecheck)
            {
                await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
                    .ConfigureAwait(false);
                nextRecheck = now.Add(recheckInterval);
            }

            yield return MapItem(item);
        }
    }

    private static string BuildTitle(LogSearchItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.ExceptionType))
        {
            return item.ExceptionType!;
        }

        var firstLine = item.Message.Split('\n', 2)[0].Trim();
        if (firstLine.Length == 0)
        {
            return "(无消息)";
        }

        return firstLine.Length <= 120 ? firstLine : firstLine[..120];
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
            TraceId = item.TraceId,
            ExceptionType = item.ExceptionType
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
