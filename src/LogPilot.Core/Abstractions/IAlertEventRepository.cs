using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IAlertEventRepository
{
    Task<AlertEvent?> FindActiveByFingerprintAsync(long ruleId, string fingerprint, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlertEvent>> ListByProjectAsync(
        long projectId,
        AlertEventStatus? status,
        int limit,
        long? cursor,
        CancellationToken cancellationToken = default);

    Task<long> InsertOrUpdateAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(long id, AlertEventStatus status, DateTimeOffset? resolvedAt, CancellationToken cancellationToken = default);

    Task UpdateNotifyInfoAsync(long id, DateTimeOffset lastNotifiedAt, int notifyCount, CancellationToken cancellationToken = default);
}
