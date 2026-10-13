namespace LogPilot.Application.Identity;

public sealed class SetupAdminRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }

    public string? DisplayName { get; init; }
}

public sealed class LoginRequest
{
    public required string Username { get; init; }

    public required string Password { get; init; }
}

public sealed class CurrentUserDto
{
    public required long Id { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required bool IsSystemAdmin { get; init; }
}
