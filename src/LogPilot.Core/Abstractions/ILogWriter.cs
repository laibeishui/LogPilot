using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

/// <summary>
/// 将归一化日志批次写入 VictoriaLogs。
/// </summary>
public interface ILogWriter
{
    Task<LogWriteResult> WriteAsync(
        IReadOnlyList<NormalizedLog> logs,
        CancellationToken cancellationToken = default);
}

public sealed class LogWriteResult
{
    public required bool Succeeded { get; init; }

    public int AcceptedCount { get; init; }

    public int RejectedCount { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsRetryable { get; init; }

    public static LogWriteResult Success(int accepted) => new()
    {
        Succeeded = true,
        AcceptedCount = accepted
    };

    public static LogWriteResult Failure(string message, bool retryable) => new()
    {
        Succeeded = false,
        ErrorMessage = message,
        IsRetryable = retryable
    };
}
