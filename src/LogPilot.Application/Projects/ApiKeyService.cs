using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Projects;

public sealed class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _apiKeys;
    private readonly IProjectRepository _projects;
    private readonly IProjectAccessService _access;
    private readonly IApiKeyGenerator _generator;

    public ApiKeyService(
        IApiKeyRepository apiKeys,
        IProjectRepository projects,
        IProjectAccessService access,
        IApiKeyGenerator generator)
    {
        _apiKeys = apiKeys;
        _projects = projects;
        _access = access;
        _generator = generator;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var keys = await _apiKeys.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return keys.Select(MapMeta).ToList();
    }

    public async Task<ApiKeyDto> CreateAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CreateApiKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageKeysAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var project = await _projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "项目不存在。", 404);

        if (project.Status != EntityStatus.Enabled)
        {
            throw new AppException("CONFLICT", "停用项目不能创建接入密钥。", 409);
        }

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "密钥名称必填且不超过 100 字符。");
        }

        if (request.ExpiresAt is not null && request.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new AppException("INVALID_ARGUMENT", "过期时间必须晚于当前时间。");
        }

        var generated = _generator.Create();
        var now = DateTimeOffset.UtcNow;
        var entity = new ApiKey
        {
            ProjectId = projectId,
            Name = name,
            KeyPrefix = generated.Prefix,
            KeyHash = generated.Hash,
            Scope = "ingest",
            Status = EntityStatus.Enabled,
            CreatedBy = userId,
            CreatedAt = now,
            ExpiresAt = request.ExpiresAt
        };

        var id = await _apiKeys.InsertAsync(entity, cancellationToken).ConfigureAwait(false);
        var saved = (await _apiKeys.FindByIdAsync(id, cancellationToken).ConfigureAwait(false))!;
        var dto = MapMeta(saved);
        return new ApiKeyDto
        {
            Id = dto.Id,
            Name = dto.Name,
            Prefix = dto.Prefix,
            Status = dto.Status,
            CreatedAt = dto.CreatedAt,
            ExpiresAt = dto.ExpiresAt,
            RevokedAt = dto.RevokedAt,
            LastUsedAt = dto.LastUsedAt,
            ApiKey = generated.Plaintext
        };
    }

    public async Task RevokeAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        long keyId,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageKeysAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var key = await _apiKeys.FindByIdAsync(keyId, cancellationToken).ConfigureAwait(false);
        if (key is null || key.ProjectId != projectId)
        {
            throw new AppException("NOT_FOUND", "密钥不存在。", 404);
        }

        if (key.Status == EntityStatus.Disabled)
        {
            return;
        }

        await _apiKeys.RevokeAsync(keyId, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    private static ApiKeyDto MapMeta(ApiKey key) => new()
    {
        Id = key.Id,
        Name = key.Name,
        Prefix = key.KeyPrefix,
        Status = key.Status == EntityStatus.Enabled && key.RevokedAt is null ? "enabled" : "revoked",
        CreatedAt = key.CreatedAt,
        ExpiresAt = key.ExpiresAt,
        RevokedAt = key.RevokedAt,
        LastUsedAt = key.LastUsedAt
    };
}
