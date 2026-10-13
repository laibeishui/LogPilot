namespace LogPilot.Application.Projects;

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyDto>> ListAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task<ApiKeyDto> CreateAsync(long userId, bool isSystemAdmin, long projectId, CreateApiKeyRequest request, CancellationToken cancellationToken = default);

    Task RevokeAsync(long userId, bool isSystemAdmin, long projectId, long keyId, CancellationToken cancellationToken = default);
}
