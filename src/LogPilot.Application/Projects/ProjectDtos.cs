using LogPilot.Core.Enums;

namespace LogPilot.Application.Projects;

public sealed class CreateProjectRequest
{
    public required string Name { get; init; }

    public required string Code { get; init; }

    public string? Description { get; init; }
}

public sealed class UpdateProjectRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public EntityStatus? Status { get; init; }
}

public sealed class ProjectDto
{
    public required long Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>项目成员数（供列表展示）。</summary>
    public int MemberCount { get; init; }
}

public sealed class CreateApiKeyRequest
{
    public required string Name { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class ApiKeyDto
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public required string Prefix { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; init; }

    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>
    /// 仅创建响应返回一次明文密钥。
    /// </summary>
    public string? ApiKey { get; init; }
}

/// <summary>项目成员（项目视角）。</summary>
public sealed class ProjectMemberDto
{
    public required long UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>用户已授权的项目（用户视角）。</summary>
public sealed class ProjectMembershipDto
{
    public required long ProjectId { get; init; }

    public required string ProjectCode { get; init; }

    public required string ProjectName { get; init; }

    public required string Role { get; init; }
}

public sealed class AddProjectMemberRequest
{
    public long UserId { get; init; }

    public ProjectMemberRole Role { get; init; }
}

public sealed class UserMembershipsRequest
{
    /// <summary>整体替换该用户的项目授权；缺省或空数组表示不授权任何项目。</summary>
    public IReadOnlyList<UserMembershipInput>? Memberships { get; init; }
}

public sealed class UserMembershipInput
{
    public long ProjectId { get; init; }

    public ProjectMemberRole Role { get; init; }
}
