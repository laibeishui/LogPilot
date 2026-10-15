using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 告警规则（对应 lp_alert_rule）。
/// </summary>
public sealed class AlertRule
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public EntityStatus Enabled { get; init; } = EntityStatus.Enabled;

    // —— 匹配范围 ——
    public string? ServiceName { get; init; }

    public string? Environment { get; init; }

    /// <summary>JSON 字符串数组，如 ["ERROR","FATAL"]；空表示所有级别。</summary>
    public string? Levels { get; init; }

    public string? Keyword { get; init; }

    public string? ExceptionType { get; init; }

    // —— 触发条件 ——
    public required AlertConditionType ConditionType { get; init; }

    /// <summary>Count 时为条数；Ratio 时为百分比（1~100）。</summary>
    public required int Threshold { get; init; }

    public required int WindowMinutes { get; init; }

    public int ForMinutes { get; init; } = 0;

    // —— 通知策略 ——
    /// <summary>JSON 整数数组，如 [1,2,3]。</summary>
    public required string ChannelIds { get; init; }

    public required AlertSeverity Severity { get; init; } = AlertSeverity.P2;

    public int CooldownMinutes { get; init; } = 5;

    public bool NotifyRecovery { get; init; } = true;

    /// <summary>持续未恢复时重发间隔（分钟）；null 表示不重发。</summary>
    public int? RepeatMinutes { get; init; }

    /// <summary>静默截止时间；该时间之前不发送通知。null 表示未静默。</summary>
    public DateTimeOffset? SilenceUntil { get; init; }

    public long? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
