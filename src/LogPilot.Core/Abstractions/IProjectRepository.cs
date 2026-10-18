using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IProjectRepository
{
    Task<Project?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<Project?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Project>> ListAccessibleAsync(long userId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    Task<long> InsertAsync(Project project, CancellationToken cancellationToken = default);

    Task UpdateAsync(Project project, CancellationToken cancellationToken = default);

    Task AddMemberAsync(long projectId, long userId, ProjectMemberRole role, DateTimeOffset createdAt, CancellationToken cancellationToken = default);

    Task<ProjectMemberRole?> GetMemberRoleAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    /// <summary>项目成员列表（含用户名/显示名），按角色高到低、用户名排序。</summary>
    Task<IReadOnlyList<ProjectMember>> ListMembersAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>批量统计各项目成员数，供项目列表展示。</summary>
    Task<IReadOnlyDictionary<long, int>> CountMembersByProjectAsync(
        IReadOnlyList<long> projectIds,
        CancellationToken cancellationToken = default);

    /// <summary>新增成员，已存在则更新角色（保留原 created_at）。</summary>
    Task UpsertMemberAsync(long projectId, long userId, ProjectMemberRole role, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default);

    /// <summary>某用户已授权的全部项目。</summary>
    Task<IReadOnlyList<ProjectMembership>> ListMembershipsByUserAsync(
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>整体替换某用户的项目授权（事务内完成），保留未变动项目的 created_at。</summary>
    Task ReplaceMembershipsAsync(
        long userId,
        IReadOnlyList<(long ProjectId, ProjectMemberRole Role)> memberships,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
