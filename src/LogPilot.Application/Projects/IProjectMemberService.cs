namespace LogPilot.Application.Projects;

/// <summary>
/// 项目成员与「用户 ↔ 项目」授权配置（仅系统管理员可调用，Web 层以 SystemAdmin 策略保证）。
/// </summary>
public interface IProjectMemberService
{
    /// <summary>某项目的成员列表（项目视角）。</summary>
    Task<IReadOnlyList<ProjectMemberDto>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default);

    /// <summary>新增成员或更新其角色。</summary>
    Task<IReadOnlyList<ProjectMemberDto>> AddOrUpdateAsync(
        long projectId,
        AddProjectMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>移除某成员。</summary>
    Task<IReadOnlyList<ProjectMemberDto>> RemoveAsync(
        long projectId,
        long memberUserId,
        CancellationToken cancellationToken = default);

    /// <summary>某用户已授权的项目（用户视角）。</summary>
    Task<IReadOnlyList<ProjectMembershipDto>> ListByUserAsync(
        long targetUserId,
        CancellationToken cancellationToken = default);

    /// <summary>整体替换某用户的项目授权，返回替换后的结果。</summary>
    Task<IReadOnlyList<ProjectMembershipDto>> ReplaceByUserAsync(
        long targetUserId,
        UserMembershipsRequest request,
        CancellationToken cancellationToken = default);
}
