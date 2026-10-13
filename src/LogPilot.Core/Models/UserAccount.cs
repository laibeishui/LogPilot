using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 本地用户账号（对应 lp_user）。
/// </summary>
public sealed class UserAccount
{
    public long Id { get; init; }

    public required string Username { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public required string PasswordHash { get; init; }

    public EntityStatus Status { get; init; } = EntityStatus.Enabled;

    public bool IsSystemAdmin { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }
}
