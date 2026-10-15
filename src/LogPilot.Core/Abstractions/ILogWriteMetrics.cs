namespace LogPilot.Core.Abstractions;

/// <summary>
/// 日志写入指标（进程内累计，重启归零）。与 AlertEvaluationService 的运行指标一样，
/// 用于健康检查与设置页的可观察性，不落库、不引入额外中间件。
/// </summary>
public interface ILogWriteMetrics
{
    /// <summary>记录一次成功的写入批次。</summary>
    void RecordSuccess();

    /// <summary>记录一次失败的写入批次。</summary>
    void RecordFailure(int recordCount, string reason);

    /// <summary>读取当前只读快照。</summary>
    LogWriteMetricsSnapshot GetSnapshot();
}

/// <summary>日志写入指标的只读快照。</summary>
public sealed record LogWriteMetricsSnapshot
{
    /// <summary>写入批次总数（含成功与失败）。</summary>
    public long TotalRequests { get; init; }

    /// <summary>失败的写入批次数。</summary>
    public long FailedRequests { get; init; }

    /// <summary>因写入失败而丢失的日志条数。</summary>
    public long FailedRecords { get; init; }

    /// <summary>连续失败批次数；任一次成功写入后归零。</summary>
    public long ConsecutiveFailures { get; init; }

    /// <summary>最近一次失败时间。</summary>
    public DateTimeOffset? LastFailureAt { get; init; }

    /// <summary>最近一次失败原因。</summary>
    public string? LastFailureReason { get; init; }
}
