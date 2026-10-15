using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IAlertRuleRepository
{
    Task<IReadOnlyList<AlertRule>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlertRule>> ListEnabledAsync(CancellationToken cancellationToken = default);

    Task<AlertRule?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<long> InsertAsync(AlertRule rule, CancellationToken cancellationToken = default);

    Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken = default);

    Task UpdateSilenceAsync(long id, DateTimeOffset? silenceUntil, CancellationToken cancellationToken = default);

    Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
