using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using LogPilot.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteAlertRuleRepository : IAlertRuleRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteAlertRuleRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<AlertRule>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<RuleRow>(
            """
            SELECT * FROM lp_alert_rule WHERE project_id = @ProjectId ORDER BY id DESC;
            """,
            new { ProjectId = projectId });
        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<IReadOnlyList<AlertRule>> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<RuleRow>(
            """
            SELECT * FROM lp_alert_rule WHERE enabled = 1 ORDER BY id;
            """);
        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<AlertRule?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<RuleRow>(
            """
            SELECT * FROM lp_alert_rule WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel();
    }

    public async Task<long> InsertAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_alert_rule(
                project_id, name, description, enabled,
                service_name, environment, levels, keyword, exception_type,
                condition_type, threshold, window_minutes, for_minutes,
                channel_ids, severity, cooldown_minutes, notify_recovery, repeat_minutes,
                created_by, created_at, updated_at)
            VALUES (
                @ProjectId, @Name, @Description, @Enabled,
                @ServiceName, @Environment, @Levels, @Keyword, @ExceptionType,
                @ConditionType, @Threshold, @WindowMinutes, @ForMinutes,
                @ChannelIds, @Severity, @CooldownMinutes, @NotifyRecovery, @RepeatMinutes,
                @CreatedBy, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                rule.ProjectId,
                rule.Name,
                rule.Description,
                Enabled = (int)rule.Enabled,
                rule.ServiceName,
                rule.Environment,
                rule.Levels,
                rule.Keyword,
                rule.ExceptionType,
                ConditionType = ToDbCondition(rule.ConditionType),
                rule.Threshold,
                rule.WindowMinutes,
                rule.ForMinutes,
                rule.ChannelIds,
                Severity = ToDbSeverity(rule.Severity),
                rule.CooldownMinutes,
                NotifyRecovery = rule.NotifyRecovery ? 1 : 0,
                rule.RepeatMinutes,
                rule.CreatedBy,
                CreatedAt = SqliteTime.ToDb(rule.CreatedAt),
                UpdatedAt = SqliteTime.ToDb(rule.UpdatedAt)
            });
    }

    public async Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_alert_rule SET
                name = @Name, description = @Description, enabled = @Enabled,
                service_name = @ServiceName, environment = @Environment, levels = @Levels,
                keyword = @Keyword, exception_type = @ExceptionType,
                condition_type = @ConditionType, threshold = @Threshold,
                window_minutes = @WindowMinutes, for_minutes = @ForMinutes,
                channel_ids = @ChannelIds, severity = @Severity,
                cooldown_minutes = @CooldownMinutes, notify_recovery = @NotifyRecovery,
                repeat_minutes = @RepeatMinutes, updated_at = @UpdatedAt
            WHERE id = @Id;
            """,
            new
            {
                rule.Id,
                rule.Name,
                rule.Description,
                Enabled = (int)rule.Enabled,
                rule.ServiceName,
                rule.Environment,
                rule.Levels,
                rule.Keyword,
                rule.ExceptionType,
                ConditionType = ToDbCondition(rule.ConditionType),
                rule.Threshold,
                rule.WindowMinutes,
                rule.ForMinutes,
                rule.ChannelIds,
                Severity = ToDbSeverity(rule.Severity),
                rule.CooldownMinutes,
                NotifyRecovery = rule.NotifyRecovery ? 1 : 0,
                rule.RepeatMinutes,
                UpdatedAt = SqliteTime.ToDb(rule.UpdatedAt)
            });
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync("DELETE FROM lp_alert_rule WHERE id = @Id;", new { Id = id });
    }

    private static string ToDbCondition(AlertConditionType type) => type switch
    {
        AlertConditionType.Count => "count",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    private static AlertConditionType FromDbCondition(string type) => type.ToLowerInvariant() switch
    {
        "count" => AlertConditionType.Count,
        _ => throw new InvalidOperationException($"Unknown alert condition type: {type}")
    };

    private static string ToDbSeverity(AlertSeverity severity) => severity switch
    {
        AlertSeverity.P0 => "P0",
        AlertSeverity.P1 => "P1",
        AlertSeverity.P2 => "P2",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };

    private static AlertSeverity FromDbSeverity(string severity) => severity switch
    {
        "P0" => AlertSeverity.P0,
        "P1" => AlertSeverity.P1,
        "P2" => AlertSeverity.P2,
        _ => throw new InvalidOperationException($"Unknown alert severity: {severity}")
    };

    private sealed class RuleRow
    {
        public long id { get; init; }
        public long project_id { get; init; }
        public string name { get; init; } = string.Empty;
        public string description { get; init; } = string.Empty;
        public int enabled { get; init; }
        public string? service_name { get; init; }
        public string? environment { get; init; }
        public string? levels { get; init; }
        public string? keyword { get; init; }
        public string? exception_type { get; init; }
        public string condition_type { get; init; } = string.Empty;
        public int threshold { get; init; }
        public int window_minutes { get; init; }
        public int for_minutes { get; init; }
        public string channel_ids { get; init; } = string.Empty;
        public string severity { get; init; } = string.Empty;
        public int cooldown_minutes { get; init; }
        public int notify_recovery { get; init; }
        public int? repeat_minutes { get; init; }
        public long? created_by { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string updated_at { get; init; } = string.Empty;

        public AlertRule ToModel() => new()
        {
            Id = id,
            ProjectId = project_id,
            Name = name,
            Description = description,
            Enabled = (EntityStatus)enabled,
            ServiceName = service_name,
            Environment = environment,
            Levels = levels,
            Keyword = keyword,
            ExceptionType = exception_type,
            ConditionType = FromDbCondition(condition_type),
            Threshold = threshold,
            WindowMinutes = window_minutes,
            ForMinutes = for_minutes,
            ChannelIds = channel_ids,
            Severity = FromDbSeverity(severity),
            CooldownMinutes = cooldown_minutes,
            NotifyRecovery = notify_recovery == 1,
            RepeatMinutes = repeat_minutes,
            CreatedBy = created_by,
            CreatedAt = SqliteTime.FromDb(created_at),
            UpdatedAt = SqliteTime.FromDb(updated_at)
        };
    }
}
