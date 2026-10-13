using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IApiKeyRepository
{
    Task<IReadOnlyList<ApiKey>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    Task<ApiKey?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken = default);

    Task<long> InsertAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

    Task RevokeAsync(long id, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    Task TouchLastUsedAsync(long id, DateTimeOffset lastUsedAt, CancellationToken cancellationToken = default);
}
