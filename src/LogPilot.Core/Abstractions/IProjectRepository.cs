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
}
