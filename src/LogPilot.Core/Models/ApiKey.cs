using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 项目接入密钥元数据（对应 lp_api_key；不含明文）。
/// </summary>
public sealed class ApiKey
{
    public long Id { get; init; }

    public long ProjectId { get; init; }

    public required string Name { get; init; }

    public required string KeyPrefix { get; init; }

    public required string KeyHash { get; init; }

    public string Scope { get; init; } = "ingest";

    public EntityStatus Status { get; init; } = EntityStatus.Enabled;

    public long? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }
}
