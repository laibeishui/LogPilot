using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;

namespace LogPilot.Application.Ingestion;

public sealed class IngestApiKeyAuthenticator : IIngestApiKeyAuthenticator
{
    private readonly IApiKeyRepository _apiKeys;
    private readonly IProjectRepository _projects;
    private readonly IApiKeyHasher _hasher;

    public IngestApiKeyAuthenticator(
        IApiKeyRepository apiKeys,
        IProjectRepository projects,
        IApiKeyHasher hasher)
    {
        _apiKeys = apiKeys;
        _projects = projects;
        _hasher = hasher;
    }

    public async Task<IngestPrincipal> AuthenticateAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new AppException("UNAUTHORIZED", "缺少有效的接入密钥。", 401);
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("lp_", StringComparison.Ordinal))
        {
            throw new AppException("UNAUTHORIZED", "接入密钥无效。", 401);
        }

        var hash = _hasher.Hash(token);
        var key = await _apiKeys.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (key is null
            || key.Status != EntityStatus.Enabled
            || key.RevokedAt is not null
            || (key.ExpiresAt is not null && key.ExpiresAt <= DateTimeOffset.UtcNow))
        {
            throw new AppException("UNAUTHORIZED", "接入密钥无效或已失效。", 401);
        }

        var project = await _projects.FindByIdAsync(key.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null || project.Status != EntityStatus.Enabled)
        {
            throw new AppException("UNAUTHORIZED", "项目不可用，拒绝写入。", 401);
        }

        await _apiKeys.TouchLastUsedAsync(key.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

        return new IngestPrincipal
        {
            ApiKeyId = key.Id,
            ProjectId = project.Id,
            ProjectCode = project.Code,
            KeyPrefix = key.KeyPrefix
        };
    }
}
