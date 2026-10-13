using LogPilot.Core.Abstractions;

namespace LogPilot.Application.Health;

/// <summary>
/// 健康检查用例：liveness 不依赖外部存储；readiness 检查 SQLite 与 VictoriaLogs。
/// </summary>
public sealed class SystemHealthService : ISystemHealthService
{
    private readonly IHealthProbe _probe;

    public SystemHealthService(IHealthProbe probe)
    {
        _probe = probe;
    }

    public Task<SystemHealthReport> GetLiveAsync(CancellationToken cancellationToken = default)
    {
        var report = new SystemHealthReport
        {
            CheckedAt = DateTimeOffset.UtcNow,
            LogPilot = new ComponentHealth
            {
                Name = "LogPilot",
                Status = HealthStatus.Healthy,
                Detail = "process alive"
            },
            Sqlite = new ComponentHealth
            {
                Name = "SQLite",
                Status = HealthStatus.Healthy,
                Detail = "not checked for liveness"
            },
            VictoriaLogs = new ComponentHealth
            {
                Name = "VictoriaLogs",
                Status = HealthStatus.Healthy,
                Detail = "not checked for liveness"
            }
        };

        return Task.FromResult(report);
    }

    public Task<SystemHealthReport> GetReadyAsync(CancellationToken cancellationToken = default)
        => _probe.CheckAsync(cancellationToken);
}
