using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IAlertChannelRepository
{
    Task<IReadOnlyList<AlertChannel>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<AlertChannel?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<long> InsertAsync(AlertChannel channel, CancellationToken cancellationToken = default);

    Task UpdateAsync(AlertChannel channel, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
