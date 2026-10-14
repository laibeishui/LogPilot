using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 通知内容（统一模型，各渠道自行格式化）。
/// </summary>
public sealed class AlertNotification
{
    public required string Title { get; init; }

    public required string Message { get; init; }

    public required AlertSeverity Severity { get; init; }

    public required string ProjectName { get; init; }

    public string? ServiceName { get; init; }

    public required string RuleName { get; init; }

    public required DateTimeOffset TriggeredAt { get; init; }

    public required AlertEventStatus Status { get; init; }

    public IReadOnlyList<SampleLog> SampleLogs { get; init; } = Array.Empty<SampleLog>();
}

public sealed class SampleLog
{
    public required string Timestamp { get; init; }

    public required string Level { get; init; }

    public required string ServiceName { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }
}

public sealed class NotifyResult
{
    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }
}
