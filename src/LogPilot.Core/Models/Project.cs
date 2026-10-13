using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 项目实体（对应 lp_project）。
/// </summary>
public sealed class Project
{
    public long Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public EntityStatus Status { get; init; } = EntityStatus.Enabled;

    public long? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
