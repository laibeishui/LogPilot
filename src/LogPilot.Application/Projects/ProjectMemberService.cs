using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Projects;

public sealed class ProjectMemberService : IProjectMemberService
{
    private readonly IProjectRepository _projects;
    private readonly IUserRepository _users;

    public ProjectMemberService(IProjectRepository projects, IUserRepository users)
    {
        _projects = projects;
        _users = users;
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> ListByProjectAsync(
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken).ConfigureAwait(false);
        var members = await _projects.ListMembersAsync(projectId, cancellationToken).ConfigureAwait(false);
        return members.Select(MapMember).ToList();
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> AddOrUpdateAsync(
        long projectId,
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken).ConfigureAwait(false);

        var user = await _users.FindByIdAsync(request.UserId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "用户不存在。", 404);

        await _projects.UpsertMemberAsync(projectId, user.Id, request.Role, cancellationToken)
            .ConfigureAwait(false);

        return await ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectMemberDto>> RemoveAsync(
        long projectId,
        long memberUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectExistsAsync(projectId, cancellationToken).ConfigureAwait(false);
        await _projects.RemoveMemberAsync(projectId, memberUserId, cancellationToken).ConfigureAwait(false);
        return await ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ProjectMembershipDto>> ListByUserAsync(
        long targetUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureUserExistsAsync(targetUserId, cancellationToken).ConfigureAwait(false);
        var items = await _projects.ListMembershipsByUserAsync(targetUserId, cancellationToken)
            .ConfigureAwait(false);
        return items.Select(MapMembership).ToList();
    }

    public async Task<IReadOnlyList<ProjectMembershipDto>> ReplaceByUserAsync(
        long targetUserId,
        UserMembershipsRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureUserExistsAsync(targetUserId, cancellationToken).ConfigureAwait(false);

        var inputs = request.Memberships ?? [];
        var seen = new HashSet<long>();
        var normalized = new List<(long ProjectId, ProjectMemberRole Role)>(inputs.Count);

        foreach (var input in inputs)
        {
            if (!seen.Add(input.ProjectId))
            {
                throw new AppException("INVALID_ARGUMENT", "同一项目重复授权。");
            }

            await EnsureProjectExistsAsync(input.ProjectId, cancellationToken).ConfigureAwait(false);
            normalized.Add((input.ProjectId, input.Role));
        }

        await _projects
            .ReplaceMembershipsAsync(targetUserId, normalized, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        return await ListByUserAsync(targetUserId, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureProjectExistsAsync(long projectId, CancellationToken cancellationToken)
    {
        var project = await _projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            throw new AppException("NOT_FOUND", "项目不存在。", 404);
        }
    }

    private async Task EnsureUserExistsAsync(long userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            throw new AppException("NOT_FOUND", "用户不存在。", 404);
        }
    }

    private static ProjectMemberDto MapMember(ProjectMember member) => new()
    {
        UserId = member.UserId,
        Username = member.Username,
        DisplayName = member.DisplayName,
        Role = ToApiRole(member.Role),
        CreatedAt = member.CreatedAt
    };

    private static ProjectMembershipDto MapMembership(ProjectMembership membership) => new()
    {
        ProjectId = membership.ProjectId,
        ProjectCode = membership.ProjectCode,
        ProjectName = membership.ProjectName,
        Role = ToApiRole(membership.Role)
    };

    private static string ToApiRole(ProjectMemberRole role) => role switch
    {
        ProjectMemberRole.Owner => "owner",
        ProjectMemberRole.Admin => "admin",
        _ => "viewer"
    };
}
