using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 项目下的应用（服务标识）实体（对应 lp_application）。
/// </summary>
public sealed class ProjectApplication
{
    public long Id { get; init; }

    public required long ProjectId { get; init; }

    public required string ServiceName { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public EntityStatus Status { get; init; } = EntityStatus.Enabled;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
