using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 通知渠道（对应 lp_alert_channel）。config_json 包含各渠道私密配置，存储时已加密。
/// </summary>
public sealed class AlertChannel
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public required string Name { get; init; }

    public required AlertChannelType Type { get; init; }

    /// <summary>
    /// 加密后的配置 JSON。
    /// </summary>
    public required string ConfigJson { get; init; }

    public EntityStatus Enabled { get; init; } = EntityStatus.Enabled;

    public long? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
