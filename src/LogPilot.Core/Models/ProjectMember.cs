using LogPilot.Core.Enums;

namespace LogPilot.Core.Models;

/// <summary>
/// 项目成员关系（对应 lp_project_member），附带用户展示信息。
/// </summary>
public sealed class ProjectMember
{
    public required long ProjectId { get; init; }

    public required long UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required ProjectMemberRole Role { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// 某用户已授权的项目（lp_project_member 关联 lp_project）。
/// </summary>
public sealed class ProjectMembership
{
    public required long ProjectId { get; init; }

    public required string ProjectCode { get; init; }

    public required string ProjectName { get; init; }

    public required ProjectMemberRole Role { get; init; }
}
