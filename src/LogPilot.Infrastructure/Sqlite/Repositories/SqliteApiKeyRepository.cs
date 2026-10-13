using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteApiKeyRepository : IApiKeyRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteApiKeyRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<ApiKey>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<ApiKeyRow>(
            """
            SELECT id, project_id, name, key_prefix, key_hash, scope, status,
                   created_by, created_at, expires_at, revoked_at, last_used_at
            FROM lp_api_key
            WHERE project_id = @ProjectId
            ORDER BY id DESC;
            """,
            new { ProjectId = projectId });
        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<ApiKey?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ApiKeyRow>(
            """
            SELECT id, project_id, name, key_prefix, key_hash, scope, status,
                   created_by, created_at, expires_at, revoked_at, last_used_at
            FROM lp_api_key WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel();
    }

    public async Task<ApiKey?> FindByHashAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ApiKeyRow>(
            """
            SELECT id, project_id, name, key_prefix, key_hash, scope, status,
                   created_by, created_at, expires_at, revoked_at, last_used_at
            FROM lp_api_key WHERE key_hash = @KeyHash LIMIT 1;
            """,
            new { KeyHash = keyHash });
        return row?.ToModel();
    }

    public async Task<long> InsertAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_api_key(
                project_id, name, key_prefix, key_hash, scope, status,
                created_by, created_at, expires_at, revoked_at, last_used_at)
            VALUES (
                @ProjectId, @Name, @KeyPrefix, @KeyHash, @Scope, @Status,
                @CreatedBy, @CreatedAt, @ExpiresAt, @RevokedAt, @LastUsedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                apiKey.ProjectId,
                apiKey.Name,
                apiKey.KeyPrefix,
                apiKey.KeyHash,
                apiKey.Scope,
                Status = (int)apiKey.Status,
                apiKey.CreatedBy,
                CreatedAt = SqliteTime.ToDb(apiKey.CreatedAt),
                ExpiresAt = apiKey.ExpiresAt is null ? null : SqliteTime.ToDb(apiKey.ExpiresAt.Value),
                RevokedAt = apiKey.RevokedAt is null ? null : SqliteTime.ToDb(apiKey.RevokedAt.Value),
                LastUsedAt = apiKey.LastUsedAt is null ? null : SqliteTime.ToDb(apiKey.LastUsedAt.Value)
            });
    }

    public async Task RevokeAsync(long id, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_api_key
            SET status = 0, revoked_at = @RevokedAt
            WHERE id = @Id;
            """,
            new { Id = id, RevokedAt = SqliteTime.ToDb(revokedAt) });
    }

    public async Task TouchLastUsedAsync(long id, DateTimeOffset lastUsedAt, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_api_key SET last_used_at = @LastUsedAt WHERE id = @Id;
            """,
            new { Id = id, LastUsedAt = SqliteTime.ToDb(lastUsedAt) });
    }

    private sealed class ApiKeyRow
    {
        public long id { get; init; }
        public long project_id { get; init; }
        public string name { get; init; } = string.Empty;
        public string key_prefix { get; init; } = string.Empty;
        public string key_hash { get; init; } = string.Empty;
        public string scope { get; init; } = "ingest";
        public int status { get; init; }
        public long? created_by { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string? expires_at { get; init; }
        public string? revoked_at { get; init; }
        public string? last_used_at { get; init; }

        public ApiKey ToModel() => new()
        {
            Id = id,
            ProjectId = project_id,
            Name = name,
            KeyPrefix = key_prefix,
            KeyHash = key_hash,
            Scope = scope,
            Status = (EntityStatus)status,
            CreatedBy = created_by,
            CreatedAt = SqliteTime.FromDb(created_at),
            ExpiresAt = SqliteTime.FromDbNullable(expires_at),
            RevokedAt = SqliteTime.FromDbNullable(revoked_at),
            LastUsedAt = SqliteTime.FromDbNullable(last_used_at)
        };
    }
}
