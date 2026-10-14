namespace LogPilot.Application.Logs;

/// <summary>
/// 日志检索请求（浏览器/控制台 → LogPilot）。
/// </summary>
public sealed class LogSearchRequestDto
{
    public long ProjectId { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public List<string>? Services { get; set; }

    public List<string>? Levels { get; set; }

    public string? Keyword { get; set; }

    public string? TraceId { get; set; }

    public int? PageSize { get; set; }

    public string? Cursor { get; set; }
}

public sealed class LogSearchResponseDto
{
    public required IReadOnlyList<LogSearchItemDto> Items { get; init; }

    public string? NextCursor { get; init; }

    public bool HasMore { get; init; }
}

public sealed class LogSearchItemDto
{
    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string ServiceName { get; init; }

    public required string Level { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }
}

public sealed class LogDetailDto
{
    public required string Id { get; init; }

    public required long ProjectId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public required string ServiceName { get; init; }

    public string Environment { get; init; } = string.Empty;

    public required string Level { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? HostName { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? ExceptionStack { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();
}

public sealed class LogFiltersDto
{
    public required IReadOnlyList<string> Services { get; init; }

    public required IReadOnlyList<string> Levels { get; init; }
}

/// <summary>
/// 单项目日志概览：近 N 分钟内的日志总量、错误量、服务数。
/// </summary>
public sealed class StatsOverviewDto
{
    public long ProjectId { get; set; }

    public long TotalLogs { get; set; }

    public long ErrorLogs { get; set; }

    public int ServiceCount { get; set; }

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset EndTime { get; set; }
}

/// <summary>
/// 单项目日志趋势。
/// </summary>
public sealed class StatsTrendDto
{
    public required string Interval { get; set; }

    public required IReadOnlyList<TrendPointDto> Points { get; set; }
}

public sealed class TrendPointDto
{
    public required DateTimeOffset Time { get; set; }

    public long Count { get; set; }

    public long Errors { get; set; }
}
