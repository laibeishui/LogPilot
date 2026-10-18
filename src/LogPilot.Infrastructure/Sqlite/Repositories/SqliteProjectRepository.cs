using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Data.Sqlite;

namespace LogPilot.Infrastructure.Sqlite.Repositories;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly ISqliteConnectionFactory _connections;

    public SqliteProjectRepository(ISqliteConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<Project?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ProjectRow>(
            """
            SELECT id, code, name, description, status, created_by, created_at, updated_at
            FROM lp_project WHERE id = @Id LIMIT 1;
            """,
            new { Id = id });
        return row?.ToModel();
    }

    public async Task<Project?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var row = await db.QuerySingleOrDefaultAsync<ProjectRow>(
            """
            SELECT id, code, name, description, status, created_by, created_at, updated_at
            FROM lp_project WHERE code = @Code LIMIT 1;
            """,
            new { Code = code });
        return row?.ToModel();
    }

    public async Task<IReadOnlyList<Project>> ListAccessibleAsync(
        long userId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        IEnumerable<ProjectRow> rows;
        if (isSystemAdmin)
        {
            rows = await db.QueryAsync<ProjectRow>(
                """
                SELECT id, code, name, description, status, created_by, created_at, updated_at
                FROM lp_project
                ORDER BY id DESC;
                """);
        }
        else
        {
            rows = await db.QueryAsync<ProjectRow>(
                """
                SELECT p.id, p.code, p.name, p.description, p.status, p.created_by, p.created_at, p.updated_at
                FROM lp_project p
                INNER JOIN lp_project_member m ON m.project_id = p.id
                WHERE m.user_id = @UserId
                ORDER BY p.id DESC;
                """,
                new { UserId = userId });
        }

        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<long> InsertAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        return await db.ExecuteScalarAsync<long>(
            """
            INSERT INTO lp_project(code, name, description, status, created_by, created_at, updated_at)
            VALUES (@Code, @Name, @Description, @Status, @CreatedBy, @CreatedAt, @UpdatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                project.Code,
                project.Name,
                project.Description,
                Status = (int)project.Status,
                project.CreatedBy,
                CreatedAt = SqliteTime.ToDb(project.CreatedAt),
                UpdatedAt = SqliteTime.ToDb(project.UpdatedAt)
            });
    }

    public async Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            UPDATE lp_project
            SET name = @Name,
                description = @Description,
                status = @Status,
                updated_at = @UpdatedAt
            WHERE id = @Id;
            """,
            new
            {
                project.Id,
                project.Name,
                project.Description,
                Status = (int)project.Status,
                UpdatedAt = SqliteTime.ToDb(project.UpdatedAt)
            });
    }

    public async Task AddMemberAsync(
        long projectId,
        long userId,
        ProjectMemberRole role,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            INSERT INTO lp_project_member(project_id, user_id, role, created_at)
            VALUES (@ProjectId, @UserId, @Role, @CreatedAt);
            """,
            new
            {
                ProjectId = projectId,
                UserId = userId,
                Role = ToDbRole(role),
                CreatedAt = SqliteTime.ToDb(createdAt)
            });
    }

    public async Task<ProjectMemberRole?> GetMemberRoleAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var role = await db.QuerySingleOrDefaultAsync<string>(
            """
            SELECT role FROM lp_project_member
            WHERE project_id = @ProjectId AND user_id = @UserId
            LIMIT 1;
            """,
            new { ProjectId = projectId, UserId = userId });

        return role is null ? null : FromDbRole(role);
    }

    public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<MemberRow>(
            """
            SELECT m.project_id, m.user_id, u.username, u.display_name, m.role, m.created_at
            FROM lp_project_member m
            INNER JOIN lp_user u ON u.id = m.user_id
            WHERE m.project_id = @ProjectId
            ORDER BY CASE m.role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, u.username;
            """,
            new { ProjectId = projectId });

        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task<IReadOnlyDictionary<long, int>> CountMembersByProjectAsync(
        IReadOnlyList<long> projectIds,
        CancellationToken cancellationToken = default)
    {
        if (projectIds.Count == 0)
        {
            return new Dictionary<long, int>();
        }

        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<ProjectCountRow>(
            """
            SELECT project_id, COUNT(1) AS member_count
            FROM lp_project_member
            WHERE project_id IN @ProjectIds
            GROUP BY project_id;
            """,
            new { ProjectIds = projectIds.Distinct().ToArray() });

        return rows.ToDictionary(x => x.project_id, x => x.member_count);
    }

    public async Task UpsertMemberAsync(
        long projectId,
        long userId,
        ProjectMemberRole role,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            INSERT INTO lp_project_member(project_id, user_id, role, created_at)
            VALUES (@ProjectId, @UserId, @Role, @CreatedAt)
            ON CONFLICT(project_id, user_id) DO UPDATE SET role = excluded.role;
            """,
            new
            {
                ProjectId = projectId,
                UserId = userId,
                Role = ToDbRole(role),
                CreatedAt = SqliteTime.ToDb(DateTimeOffset.UtcNow)
            });
    }

    public async Task RemoveMemberAsync(
        long projectId,
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await db.ExecuteAsync(
            """
            DELETE FROM lp_project_member
            WHERE project_id = @ProjectId AND user_id = @UserId;
            """,
            new { ProjectId = projectId, UserId = userId });
    }

    public async Task<IReadOnlyList<ProjectMembership>> ListMembershipsByUserAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        var rows = await db.QueryAsync<MembershipRow>(
            """
            SELECT p.id AS project_id, p.code AS project_code, p.name AS project_name, m.role
            FROM lp_project_member m
            INNER JOIN lp_project p ON p.id = m.project_id
            WHERE m.user_id = @UserId
            ORDER BY p.id DESC;
            """,
            new { UserId = userId });

        return rows.Select(x => x.ToModel()).ToList();
    }

    public async Task ReplaceMembershipsAsync(
        long userId,
        IReadOnlyList<(long ProjectId, ProjectMemberRole Role)> memberships,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        await using var db = (SqliteConnection)await _connections.OpenConnectionAsync(cancellationToken);
        await using var tx = await db.BeginTransactionAsync(cancellationToken);

        var projectIds = memberships.Select(m => m.ProjectId).Distinct().ToArray();
        if (projectIds.Length == 0)
        {
            await db.ExecuteAsync(
                "DELETE FROM lp_project_member WHERE user_id = @UserId;",
                new { UserId = userId }, tx);
        }
        else
        {
            await db.ExecuteAsync(
                """
                DELETE FROM lp_project_member
                WHERE user_id = @UserId AND project_id NOT IN @ProjectIds;
                """,
                new { UserId = userId, ProjectIds = projectIds }, tx);
        }

        foreach (var (projectId, role) in memberships)
        {
            // ON CONFLICT 只更新角色，保留原有 created_at。
            await db.ExecuteAsync(
                """
                INSERT INTO lp_project_member(project_id, user_id, role, created_at)
                VALUES (@ProjectId, @UserId, @Role, @CreatedAt)
                ON CONFLICT(project_id, user_id) DO UPDATE SET role = excluded.role;
                """,
                new
                {
                    ProjectId = projectId,
                    UserId = userId,
                    Role = ToDbRole(role),
                    CreatedAt = SqliteTime.ToDb(updatedAt)
                },
                tx);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static string ToDbRole(ProjectMemberRole role) => role switch
    {
        ProjectMemberRole.Owner => "owner",
        ProjectMemberRole.Admin => "admin",
        ProjectMemberRole.Viewer => "viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };

    private static ProjectMemberRole FromDbRole(string role) => role.ToLowerInvariant() switch
    {
        "owner" => ProjectMemberRole.Owner,
        "admin" => ProjectMemberRole.Admin,
        "viewer" => ProjectMemberRole.Viewer,
        _ => throw new InvalidOperationException($"Unknown project role: {role}")
    };

    private sealed class ProjectRow
    {
        public long id { get; init; }
        public string code { get; init; } = string.Empty;
        public string name { get; init; } = string.Empty;
        public string description { get; init; } = string.Empty;
        public int status { get; init; }
        public long? created_by { get; init; }
        public string created_at { get; init; } = string.Empty;
        public string updated_at { get; init; } = string.Empty;

        public Project ToModel() => new()
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            Status = (EntityStatus)status,
            CreatedBy = created_by,
            CreatedAt = SqliteTime.FromDb(created_at),
            UpdatedAt = SqliteTime.FromDb(updated_at)
        };
    }

    private sealed class MemberRow
    {
        public long project_id { get; init; }
        public long user_id { get; init; }
        public string username { get; init; } = string.Empty;
        public string display_name { get; init; } = string.Empty;
        public string role { get; init; } = string.Empty;
        public string created_at { get; init; } = string.Empty;

        public ProjectMember ToModel() => new()
        {
            ProjectId = project_id,
            UserId = user_id,
            Username = username,
            DisplayName = display_name,
            Role = FromDbRole(role),
            CreatedAt = SqliteTime.FromDb(created_at)
        };
    }

    private sealed class ProjectCountRow
    {
        public long project_id { get; init; }
        public int member_count { get; init; }
    }

    private sealed class MembershipRow
    {
        public long project_id { get; init; }
        public string project_code { get; init; } = string.Empty;
        public string project_name { get; init; } = string.Empty;
        public string role { get; init; } = string.Empty;

        public ProjectMembership ToModel() => new()
        {
            ProjectId = project_id,
            ProjectCode = project_code,
            ProjectName = project_name,
            Role = FromDbRole(role)
        };
    }
}
