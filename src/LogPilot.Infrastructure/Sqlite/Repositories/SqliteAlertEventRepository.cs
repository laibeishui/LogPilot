using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using LogPilot.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteAlertEventRepository : IAlertEventRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteAlertEventRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<AlertEvent?> FindActiveByFingerprintAsync(long ruleId, string fingerprint, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<EventRow>(
            """
            SELECT * FROM lp_alert_event
            WHERE rule_id = @RuleId AND fingerprint = @Fingerprint AND status IN ('firing','acknowledged','silenced')
            LIMIT 1;
            """,
            new { RuleId = ruleId, Fingerprint = fingerprint });
        return row?.ToModel();
    }

    public async Task<IReadOnlyList<AlertEvent>> ListByProjectAsync(
        long projectId,
        AlertEventStatus? status,
        int limit,
        long? cursor,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);

        var sql = """
            SELECT * FROM lp_alert_event WHERE project_id = @ProjectId
            """;
        var param = new DynamicParameters();
        param.Add("ProjectId", projectId);

        if (status.HasValue)
        {
            sql += " AND status = @Status";
            param.Add("Status", ToDbStatus(status.Value));
        }

        if (cursor.HasValue)
        {
            sql += " AND id < @Cursor";
            param.Add("Cursor", cursor.Value);
        }

        sql += " ORDER BY id DESC LIMIT @Limit";
        param.Add("Limit", limit);

        var rows = await db.QueryAsync<EventRow>(sql, param);
        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<long> InsertOrUpdateAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);

        var existing = await db.QuerySingleOrDefaultAsync<long?>(
            """
            SELECT id FROM lp_alert_event WHERE rule_id = @RuleId AND fingerprint = @Fingerprint LIMIT 1;
            """,
            new { alertEvent.RuleId, alertEvent.Fingerprint });

        if (existing.HasValue)
        {
            await db.ExecuteAsync(
                """
                UPDATE lp_alert_event SET
                    status = @Status, severity = @Severity, title = @Title, message = @Message,
                    sample_logs_json = @SampleLogsJson, triggered_at = @TriggeredAt,
                    acknowledged_at = @AcknowledgedAt, resolved_at = @ResolvedAt,
                    last_notified_at = @LastNotifiedAt, notify_count = @NotifyCount
                WHERE id = @Id;
                """,
                new
                {
                    Id = existing.Value,
                    Status = ToDbStatus(alertEvent.Status),
                    Severity = ToDbSeverity(alertEvent.Severity),
                    alertEvent.Title,
                    alertEvent.Message,
                    alertEvent.SampleLogsJson,
                    TriggeredAt = SqliteTime.ToDb(alertEvent.TriggeredAt),
                    AcknowledgedAt = alertEvent.AcknowledgedAt.HasValue ? SqliteTime.ToDb(alertEvent.AcknowledgedAt.Value) : null,
                    ResolvedAt = alertEvent.ResolvedAt.HasValue ? SqliteTime.ToDb(alertEvent.ResolvedAt.Value) : null,
                    LastNotifiedAt = alertEvent.LastNotifiedAt.HasValue ? SqliteTime.ToDb(alertEvent.LastNotifiedAt.Value) : null,
                    alertEvent.NotifyCount
                });
            return existing.Value;
        }

        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_alert_event(
                rule_id, project_id, fingerprint, status, severity, title, message,
                sample_logs_json, triggered_at, acknowledged_at, resolved_at,
                last_notified_at, notify_count)
            VALUES (
                @RuleId, @ProjectId, @Fingerprint, @Status, @Severity, @Title, @Message,
                @SampleLogsJson, @TriggeredAt, @AcknowledgedAt, @ResolvedAt,
                @LastNotifiedAt, @NotifyCount);
            SELECT last_insert_rowid();
            """,
            new
            {
                alertEvent.RuleId,
                alertEvent.ProjectId,
                alertEvent.Fingerprint,
                Status = ToDbStatus(alertEvent.Status),
                Severity = ToDbSeverity(alertEvent.Severity),
                alertEvent.Title,
                alertEvent.Message,
                alertEvent.SampleLogsJson,
                TriggeredAt = SqliteTime.ToDb(alertEvent.TriggeredAt),
                AcknowledgedAt = alertEvent.AcknowledgedAt.HasValue ? SqliteTime.ToDb(alertEvent.AcknowledgedAt.Value) : null,
                ResolvedAt = alertEvent.ResolvedAt.HasValue ? SqliteTime.ToDb(alertEvent.ResolvedAt.Value) : null,
                LastNotifiedAt = alertEvent.LastNotifiedAt.HasValue ? SqliteTime.ToDb(alertEvent.LastNotifiedAt.Value) : null,
                alertEvent.NotifyCount
            });
    }

    public async Task UpdateStatusAsync(long id, AlertEventStatus status, DateTimeOffset? resolvedAt, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_alert_event
            SET status = @Status, resolved_at = @ResolvedAt
            WHERE id = @Id;
            """,
            new
            {
                Id = id,
                Status = ToDbStatus(status),
                ResolvedAt = resolvedAt.HasValue ? SqliteTime.ToDb(resolvedAt.Value) : null
            });
    }

    public async Task UpdateNotifyInfoAsync(long id, DateTimeOffset lastNotifiedAt, int notifyCount, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_alert_event SET last_notified_at = @LastNotifiedAt, notify_count = @NotifyCount WHERE id = @Id;
            """,
            new
            {
                Id = id,
                LastNotifiedAt = SqliteTime.ToDb(lastNotifiedAt),
                NotifyCount = notifyCount
            });
    }

    private static string ToDbStatus(AlertEventStatus status) => status switch
    {
        AlertEventStatus.Firing => "firing",
        AlertEventStatus.Acknowledged => "acknowledged",
        AlertEventStatus.Resolved => "resolved",
        AlertEventStatus.Silenced => "silenced",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static AlertEventStatus FromDbStatus(string status) => status.ToLowerInvariant() switch
    {
        "firing" => AlertEventStatus.Firing,
        "acknowledged" => AlertEventStatus.Acknowledged,
        "resolved" => AlertEventStatus.Resolved,
        "silenced" => AlertEventStatus.Silenced,
        _ => throw new InvalidOperationException($"Unknown alert event status: {status}")
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

    private sealed class EventRow
    {
        public long id { get; init; }
        public long rule_id { get; init; }
        public long project_id { get; init; }
        public string fingerprint { get; init; } = string.Empty;
        public string status { get; init; } = string.Empty;
        public string severity { get; init; } = string.Empty;
        public string title { get; init; } = string.Empty;
        public string message { get; init; } = string.Empty;
        public string? sample_logs_json { get; init; }
        public string triggered_at { get; init; } = string.Empty;
        public string? acknowledged_at { get; init; }
        public string? resolved_at { get; init; }
        public string? last_notified_at { get; init; }
        public int notify_count { get; init; }

        public AlertEvent ToModel() => new()
        {
            Id = id,
            RuleId = rule_id,
            ProjectId = project_id,
            Fingerprint = fingerprint,
            Status = FromDbStatus(status),
            Severity = FromDbSeverity(severity),
            Title = title,
            Message = message,
            SampleLogsJson = sample_logs_json,
            TriggeredAt = SqliteTime.FromDb(triggered_at),
            AcknowledgedAt = SqliteTime.FromDbNullable(acknowledged_at),
            ResolvedAt = SqliteTime.FromDbNullable(resolved_at),
            LastNotifiedAt = SqliteTime.FromDbNullable(last_notified_at),
            NotifyCount = notify_count
        };
    }
}
