namespace LogPilot.Application.Identity;

public sealed class UserDto
{
    public required long Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string Status { get; init; }

    public required bool IsSystemAdmin { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }
}

/// <summary>人员管理列表（分页）。</summary>
public sealed class UserListResult
{
    public required IReadOnlyList<UserDto> Items { get; init; }

    public required int Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

public sealed class CreateUserRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }

    public string? DisplayName { get; init; }

    public bool IsSystemAdmin { get; init; }
}

/// <summary>仅更新显式提供的字段。</summary>
public sealed class UpdateUserRequest
{
    public string? DisplayName { get; init; }

    public bool? IsSystemAdmin { get; init; }

    public bool? Enabled { get; init; }
}

/// <summary>管理员重置指定用户密码。</summary>
public sealed class ResetPasswordRequest
{
    public required string Password { get; init; }
}

/// <summary>用户修改自己的密码。</summary>
public sealed class ChangePasswordRequest
{
    public required string CurrentPassword { get; init; }

    public required string NewPassword { get; init; }
}
