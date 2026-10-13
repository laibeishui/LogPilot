namespace LogPilot.Core.Abstractions;

/// <summary>
/// 运行期健康探测（SQLite / VictoriaLogs）。
/// </summary>
public interface IHealthProbe
{
    Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class SystemHealthReport
{
    public required DateTimeOffset CheckedAt { get; init; }

    public required ComponentHealth LogPilot { get; init; }

    public required ComponentHealth Sqlite { get; init; }

    public required ComponentHealth VictoriaLogs { get; init; }

    public bool IsReady =>
        LogPilot.Status == HealthStatus.Healthy
        && Sqlite.Status == HealthStatus.Healthy
        && VictoriaLogs.Status == HealthStatus.Healthy;
}

public sealed class ComponentHealth
{
    public required string Name { get; init; }

    public required HealthStatus Status { get; init; }

    public string? Detail { get; init; }
}

public enum HealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}
