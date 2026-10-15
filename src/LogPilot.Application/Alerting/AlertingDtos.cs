using LogPilot.Core.Enums;

namespace LogPilot.Application.Alerting;

// —— 渠道 DTO ——
public sealed class CreateAlertChannelRequest
{
    public required string Name { get; init; }

    public required AlertChannelType Type { get; init; }

    /// <summary>渠道配置 JSON（明文，服务端加密存储）。</summary>
    public required string ConfigJson { get; init; }

    public bool? Enabled { get; init; }
}

public sealed class UpdateAlertChannelRequest
{
    public string? Name { get; init; }

    public AlertChannelType? Type { get; init; }

    /// <summary>非空则更新配置。</summary>
    public string? ConfigJson { get; init; }

    public bool? Enabled { get; init; }
}

public sealed class AlertChannelDto
{
    public required long Id { get; init; }

    public required long ProjectId { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    /// <summary>脱敏后的配置摘要（不含密钥）。</summary>
    public required string ConfigSummary { get; init; }

    public required bool Enabled { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

// —— 规则 DTO ——
public sealed class CreateAlertRuleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public string? ServiceName { get; init; }

    public string? Environment { get; init; }

    public IReadOnlyList<string>? Levels { get; init; }

    public string? Keyword { get; init; }

    public string? ExceptionType { get; init; }

    public required AlertConditionType ConditionType { get; init; }

    public required int Threshold { get; init; }

    public required int WindowMinutes { get; init; }

    public int? ForMinutes { get; init; }

    public required IReadOnlyList<long> ChannelIds { get; init; }

    public AlertSeverity? Severity { get; init; }

    public int? CooldownMinutes { get; init; }

    public bool? NotifyRecovery { get; init; }

    public int? RepeatMinutes { get; init; }
}

public sealed class UpdateAlertRuleRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public string? ServiceName { get; init; }

    public string? Environment { get; init; }

    public IReadOnlyList<string>? Levels { get; init; }

    public string? Keyword { get; init; }

    public string? ExceptionType { get; init; }

    public AlertConditionType? ConditionType { get; init; }

    public int? Threshold { get; init; }

    public int? WindowMinutes { get; init; }

    public int? ForMinutes { get; init; }

    public IReadOnlyList<long>? ChannelIds { get; init; }

    public AlertSeverity? Severity { get; init; }

    public int? CooldownMinutes { get; init; }

    public bool? NotifyRecovery { get; init; }

    public int? RepeatMinutes { get; init; }
}

public sealed class AlertRuleDto
{
    public required long Id { get; init; }

    public required long ProjectId { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required bool Enabled { get; init; }

    public string? ServiceName { get; init; }

    public string? Environment { get; init; }

    public IReadOnlyList<string> Levels { get; init; } = Array.Empty<string>();

    public string? Keyword { get; init; }

    public string? ExceptionType { get; init; }

    public required string ConditionType { get; init; }

    public required int Threshold { get; init; }

    public required int WindowMinutes { get; init; }

    public required int ForMinutes { get; init; }

    public required IReadOnlyList<long> ChannelIds { get; init; }

    public required string Severity { get; init; }

    public required int CooldownMinutes { get; init; }

    public required bool NotifyRecovery { get; init; }

    public int? RepeatMinutes { get; init; }

    /// <summary>静默截止时间；早于该时间不发送通知。null 表示未静默。</summary>
    public DateTimeOffset? SilenceUntil { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>设置/取消规则静默期。Minutes 为 null 或 <=0 表示取消静默。</summary>
public sealed class SilenceAlertRuleRequest
{
    public int? Minutes { get; init; }
}

// —— 事件 DTO ——
public sealed class AlertEventDto
{
    public required long Id { get; init; }

    public required long RuleId { get; init; }

    public required long ProjectId { get; init; }

    public required string Fingerprint { get; init; }

    public required string Status { get; init; }

    public required string Severity { get; init; }

    public required string Title { get; init; }

    public required string Message { get; init; }

    public IReadOnlyList<SampleLogDto> SampleLogs { get; init; } = Array.Empty<SampleLogDto>();

    public required DateTimeOffset TriggeredAt { get; init; }

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public required int NotifyCount { get; init; }
}

public sealed class SampleLogDto
{
    public required string Timestamp { get; init; }

    public required string Level { get; init; }

    public required string ServiceName { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }
}

public sealed class AlertEventListResponse
{
    public required IReadOnlyList<AlertEventDto> Items { get; init; }

    public long? NextCursor { get; init; }

    public required bool HasMore { get; init; }
}
