namespace LogPilot.Core.Abstractions;

/// <summary>
/// 在已授权项目范围内查询 VictoriaLogs。
/// </summary>
public interface ILogSearcher
{
    Task<LogSearchResult> SearchAsync(
        AuthorizedLogQuery query,
        CancellationToken cancellationToken = default);

    Task<LogDetail?> GetByEventIdAsync(
        long projectId,
        string eventId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListServicesAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计匹配条件的日志数量（用于告警评估）。
    /// </summary>
    Task<long> CountAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string? serviceName,
        string? environment,
        IReadOnlyList<string> levels,
        string? keyword,
        string? exceptionType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按时间桶聚合日志数量（用于趋势图）。
    /// </summary>
    Task<IReadOnlyList<TrendPoint>> TrendAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string interval,
        bool errorsOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 拉取错误样本用于异常指纹聚合（按时间降序，最多 limit 条）。
    /// </summary>
    Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 实时跟随日志（VictoriaLogs tail），逐个产出新的日志事件，用于 SSE 推送。
    /// </summary>
    IAsyncEnumerable<LogSearchItem> TailAsync(
        long projectId,
        IReadOnlyList<string> services,
        IReadOnlyList<string> levels,
        string? keyword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 单时间桶的聚合结果。
/// </summary>
public sealed class TrendPoint
{
    public required DateTimeOffset Bucket { get; init; }

    public long Count { get; init; }
}

/// <summary>
/// 服务端确认过权限的查询条件；禁止由浏览器 DTO 直接转成无限制 LogsQL。
/// </summary>
public sealed class AuthorizedLogQuery
{
    public required long ProjectId { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    public required DateTimeOffset EndTime { get; init; }

    public IReadOnlyList<string> Services { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();

    public string? Keyword { get; init; }

    public string? TraceId { get; init; }

    public int PageSize { get; init; } = 50;

    /// <summary>
    /// 分页偏移：跳过的最新日志条数（offset = (page - 1) * pageSize）。
    /// </summary>
    public int Offset { get; init; }
}

public sealed class LogSearchResult
{
    public required IReadOnlyList<LogSearchItem> Items { get; init; }

    /// <summary>匹配条件的日志总数。</summary>
    public long Total { get; init; }
}

public sealed class LogSearchItem
{
    public required string Id { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string ServiceName { get; init; }

    public required string Level { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }

    public string? ExceptionType { get; init; }
}

public sealed class LogDetail
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
