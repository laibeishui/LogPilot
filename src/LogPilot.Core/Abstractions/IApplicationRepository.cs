using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

/// <summary>
/// 项目应用（服务标识）持久化。
/// </summary>
public interface IApplicationRepository
{
    Task<IReadOnlyList<ProjectApplication>> ListByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectApplication?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ProjectApplication?> FindByServiceNameAsync(
        long projectId,
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<long> InsertAsync(ProjectApplication application, CancellationToken cancellationToken = default);
}
