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
