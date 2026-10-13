using LogPilot.Core.Abstractions;

namespace LogPilot.Application.Health;

public interface ISystemHealthService
{
    Task<SystemHealthReport> GetLiveAsync(CancellationToken cancellationToken = default);

    Task<SystemHealthReport> GetReadyAsync(CancellationToken cancellationToken = default);
}
