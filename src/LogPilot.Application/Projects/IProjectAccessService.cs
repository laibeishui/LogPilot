using LogPilot.Core.Enums;

namespace LogPilot.Application.Projects;

public interface IProjectAccessService
{
    Task EnsureCanViewAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task EnsureCanManageAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task EnsureCanManageKeysAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);
}

public sealed class ProjectAccessService : IProjectAccessService
{
    private readonly Core.Abstractions.IProjectRepository _projects;

    public ProjectAccessService(Core.Abstractions.IProjectRepository projects)
    {
        _projects = projects;
    }

    public async Task EnsureCanViewAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        if (isSystemAdmin)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var role = await _projects.GetMemberRoleAsync(projectId, userId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            throw new Common.AppException("PROJECT_ACCESS_DENIED", "无权访问该项目。", 403);
        }
    }

    public async Task EnsureCanManageAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        if (isSystemAdmin)
        {
            await EnsureProjectExistsAsync(projectId, cancellationToken).ConfigureAwait(false);
            return;
        }

        var role = await _projects.GetMemberRoleAsync(projectId, userId, cancellationToken).ConfigureAwait(false);
        if (role is not (ProjectMemberRole.Owner or ProjectMemberRole.Admin))
        {
            throw new Common.AppException("PROJECT_ACCESS_DENIED", "无权管理该项目。", 403);
        }
    }

    public async Task EnsureCanManageKeysAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
        => await EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);

    private async Task EnsureProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await _projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new Common.AppException("NOT_FOUND", "项目不存在。", 404);
        }
    }
}
