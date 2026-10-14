using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 告警事件（对应 lp_alert_event）。同一 rule_id + fingerprint 只保留一条活跃记录。
/// </summary>
public sealed class AlertEvent
{
    public long Id { get; init; }

    public long RuleId { get; init; }

    public long ProjectId { get; init; }

    public required string Fingerprint { get; init; }

    public required AlertEventStatus Status { get; init; }

    public required AlertSeverity Severity { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    /// <summary>触发时的样本日志 JSON（最多 N 条）。</summary>
    public string? SampleLogsJson { get; init; }

    public required DateTimeOffset TriggeredAt { get; init; }

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public DateTimeOffset? LastNotifiedAt { get; init; }

    public int NotifyCount { get; init; } = 0;
}
