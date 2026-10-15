using LogPilot.Core.Abstractions;

namespace LogPilot.Infrastructure.Observability;

/// <summary>
/// <see cref="ILogWriteMetrics"/> 的线程安全实现。计数在进程内累计，容器重启后归零。
/// </summary>
public sealed class LogWriteMetrics : ILogWriteMetrics
{
    private long _totalRequests;
    private long _failedRequests;
    private long _failedRecords;
    private long _consecutiveFailures;
    private long _lastFailureAtUtcTicks;
    private string? _lastFailureReason;

    public void RecordSuccess()
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Exchange(ref _consecutiveFailures, 0);
    }

    public void RecordFailure(int recordCount, string reason)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);
        Interlocked.Add(ref _failedRecords, recordCount);
        Interlocked.Increment(ref _consecutiveFailures);
        Interlocked.Exchange(ref _lastFailureAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        _lastFailureReason = reason;
    }

    public LogWriteMetricsSnapshot GetSnapshot()
    {
        var ticks = Interlocked.Read(ref _lastFailureAtUtcTicks);

        return new LogWriteMetricsSnapshot
        {
            TotalRequests = Interlocked.Read(ref _totalRequests),
            FailedRequests = Interlocked.Read(ref _failedRequests),
            FailedRecords = Interlocked.Read(ref _failedRecords),
            ConsecutiveFailures = Interlocked.Read(ref _consecutiveFailures),
            LastFailureAt = ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero),
            LastFailureReason = Volatile.Read(ref _lastFailureReason)
        };
    }
}
