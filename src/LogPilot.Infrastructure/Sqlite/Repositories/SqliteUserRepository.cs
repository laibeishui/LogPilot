using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteUserRepository : IUserRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteUserRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM lp_user;");
    }

    public async Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM lp_user WHERE is_system_admin = 1 AND status = 1;");
    }

    public async Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<UserRow>(
            """
            SELECT id, username, display_name, password_hash, status, is_system_admin,
                   created_at, updated_at, last_login_at
            FROM lp_user WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel();
    }

    public async Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<UserRow>(
            """
            SELECT id, username, display_name, password_hash, status, is_system_admin,
                   created_at, updated_at, last_login_at
            FROM lp_user WHERE username = @Username LIMIT 1;
            """,
            new { Username = username });
        return row?.ToModel();
    }

    public async Task<long> InsertAsync(UserAccount user, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_user(username, display_name, password_hash, status, is_system_admin, created_at, updated_at, last_login_at)
            VALUES (@Username, @DisplayName, @PasswordHash, @Status, @IsSystemAdmin, @CreatedAt, @UpdatedAt, @LastLoginAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                user.Username,
                user.DisplayName,
                user.PasswordHash,
                Status = (int)user.Status,
                IsSystemAdmin = user.IsSystemAdmin ? 1 : 0,
                CreatedAt = SqliteTime.ToDb(user.CreatedAt),
                UpdatedAt = SqliteTime.ToDb(user.UpdatedAt),
                LastLoginAt = user.LastLoginAt is null ? null : SqliteTime.ToDb(user.LastLoginAt.Value)
            });
    }

    public async Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_user
            SET last_login_at = @LastLoginAt, updated_at = @UpdatedAt
            WHERE id = @Id;
            """,
            new
            {
                Id = userId,
                LastLoginAt = SqliteTime.ToDb(lastLoginAt),
                UpdatedAt = SqliteTime.ToDb(lastLoginAt)
            });
    }

    private sealed class UserRow
    {
        public long id { get; init; }
        public string username { get; init; } = string.Empty;
        public string display_name { get; init; } = string.Empty;
        public string password_hash { get; init; } = string.Empty;
        public int status { get; init; }
        public int is_system_admin { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string updated_at { get; init; } = string.Empty;
        public string? last_login_at { get; init; }

        public UserAccount ToModel() => new()
        {
            Id = id,
            Username = username,
            DisplayName = display_name,
            PasswordHash = password_hash,
            Status = (EntityStatus)status,
            IsSystemAdmin = is_system_admin == 1,
            CreatedAt = SqliteTime.FromDb(created_at),
            UpdatedAt = SqliteTime.FromDb(updated_at),
            LastLoginAt = SqliteTime.FromDbNullable(last_login_at)
        };
    }
}
