using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using LogPilot.Infrastructure.Security;
using LogPilot.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteAlertChannelRepository : IAlertChannelRepository
{
    private readonly ISqliteConnectionFactory _connections;
    private readonly ChannelConfigProtector _protector;

    public SqliteAlertChannelRepository(ISqliteConnectionFactory connections, ChannelConfigProtector protector)
    {
        _connections = connections;
        _protector = protector;
    }

    public async Task<IReadOnlyList<AlertChannel>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<ChannelRow>(
            """
            SELECT id, project_id, name, type, config_json, enabled, created_by, created_at, updated_at
            FROM lp_alert_channel WHERE project_id = @ProjectId ORDER BY id DESC;
            """,
            new { ProjectId = projectId });
        return rows.Select(x => x.ToModel(_protector)).ToList();
    }

    public async Task<AlertChannel?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ChannelRow>(
            """
            SELECT id, project_id, name, type, config_json, enabled, created_by, created_at, updated_at
            FROM lp_alert_channel WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel(_protector);
    }

    public async Task<long> InsertAsync(AlertChannel channel, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_alert_channel(project_id, name, type, config_json, enabled, created_by, created_at, updated_at)
            VALUES (@ProjectId, @Name, @Type, @ConfigJson, @Enabled, @CreatedBy, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                channel.ProjectId,
                channel.Name,
                Type = ToDbType(channel.Type),
                ConfigJson = _protector.Encrypt(channel.ConfigJson),
                Enabled = (int)channel.Enabled,
                channel.CreatedBy,
                CreatedAt = SqliteTime.ToDb(channel.CreatedAt),
                UpdatedAt = SqliteTime.ToDb(channel.UpdatedAt)
            });
    }

    public async Task UpdateAsync(AlertChannel channel, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_alert_channel
            SET name = @Name, type = @Type, config_json = @ConfigJson, enabled = @Enabled, updated_at = @UpdatedAt
            WHERE id = @Id;
            """,
            new
            {
                channel.Id,
                channel.Name,
                Type = ToDbType(channel.Type),
                ConfigJson = _protector.Encrypt(channel.ConfigJson),
                Enabled = (int)channel.Enabled,
                UpdatedAt = SqliteTime.ToDb(channel.UpdatedAt)
            });
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync("DELETE FROM lp_alert_channel WHERE id = @Id;", new { Id = id });
    }

    private static string ToDbType(AlertChannelType type) => type switch
    {
        AlertChannelType.DingTalk => "dingtalk",
        AlertChannelType.WeCom => "wecom",
        AlertChannelType.Email => "email",
        AlertChannelType.Webhook => "webhook",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static AlertChannelType FromDbType(string type) => type.ToLowerInvariant() switch
    {
        "dingtalk" => AlertChannelType.DingTalk,
        "wecom" => AlertChannelType.WeCom,
        "email" => AlertChannelType.Email,
        "webhook" => AlertChannelType.Webhook,
        _ => throw new InvalidOperationException($"Unknown alert channel type: {type}")
    };

    private sealed class ChannelRow
    {
        public long id { get; init; }
        public long project_id { get; init; }
        public string name { get; init; } = string.Empty;
        public string type { get; init; } = string.Empty;
        public string config_json { get; init; } = string.Empty;
        public int enabled { get; init; }
        public long? created_by { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string updated_at { get; init; } = string.Empty;

        public AlertChannel ToModel(ChannelConfigProtector protector) => new()
        {
            Id = id,
            ProjectId = project_id,
            Name = name,
            Type = FromDbType(type),
            ConfigJson = protector.Decrypt(config_json),
            Enabled = (EntityStatus)enabled,
            CreatedBy = created_by,
            CreatedAt = SqliteTime.FromDb(created_at),
            UpdatedAt = SqliteTime.FromDb(updated_at)
        };
    }
}
