namespace LogPilot.Application.Projects;

public sealed class CreateApplicationRequest
{
    public required string ServiceName { get; init; }

    public string? DisplayName { get; init; }

    public string? Description { get; init; }
}

public sealed class ApplicationDto
{
    public required long Id { get; init; }

    public required long ProjectId { get; init; }

    public required string ServiceName { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
