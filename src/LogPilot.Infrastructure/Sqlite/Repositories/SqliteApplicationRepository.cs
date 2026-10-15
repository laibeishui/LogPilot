using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteApplicationRepository : IApplicationRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteApplicationRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<IReadOnlyList<ProjectApplication>> ListByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<ApplicationRow>(
            """
            SELECT id, project_id, service_name, display_name, description, status, created_at, updated_at
            FROM lp_application
            WHERE project_id = @ProjectId
            ORDER BY service_name COLLATE NOCASE;
            """,
            new { ProjectId = projectId });
        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<ProjectApplication?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ApplicationRow>(
            """
            SELECT id, project_id, service_name, display_name, description, status, created_at, updated_at
            FROM lp_application WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel();
    }

    public async Task<ProjectApplication?> FindByServiceNameAsync(
        long projectId,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ApplicationRow>(
            """
            SELECT id, project_id, service_name, display_name, description, status, created_at, updated_at
            FROM lp_application
            WHERE project_id = @ProjectId AND service_name = @ServiceName COLLATE NOCASE
            LIMIT 1;
            """,
            new { ProjectId = projectId, ServiceName = serviceName });
        return row?.ToModel();
    }

    public async Task<long> InsertAsync(ProjectApplication application, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_application(project_id, service_name, display_name, description, status, created_at, updated_at)
            VALUES (@ProjectId, @ServiceName, @DisplayName, @Description, @Status, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                application.ProjectId,
                application.ServiceName,
                application.DisplayName,
                application.Description,
                Status = (int)application.Status,
                CreatedAt = SqliteTime.ToDb(application.CreatedAt),
                UpdatedAt = SqliteTime.ToDb(application.UpdatedAt)
            });
    }

    private sealed class ApplicationRow
    {
        public long id { get; init; }
        public long project_id { get; init; }
        public string service_name { get; init; } = string.Empty;
        public string display_name { get; init; } = string.Empty;
        public string description { get; init; } = string.Empty;
        public int status { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string updated_at { get; init; } = string.Empty;

        public ProjectApplication ToModel() => new()
        {
            Id = id,
            ProjectId = project_id,
            ServiceName = service_name,
            DisplayName = display_name,
            Description = description,
            Status = (EntityStatus)status,
            CreatedAt = SqliteTime.FromDb(created_at),
            UpdatedAt = SqliteTime.FromDb(updated_at)
        };
    }
}
