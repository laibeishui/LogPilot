namespace LogPilot.Application.Identity;

/// <summary>
/// 人员管理（仅系统管理员可调用，Web 层以 SystemAdmin 策略保证）。
/// </summary>
public interface IUserManagementService
{
    Task<UserListResult> ListAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    /// <summary>更新显示名、系统管理员标志、启用状态。<paramref name="operatorUserId"/> 用于阻止自锁。</summary>
    Task<UserDto> UpdateAsync(
        long operatorUserId,
        long userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>管理员重置他人密码。</summary>
    Task ResetPasswordAsync(long userId, ResetPasswordRequest request, CancellationToken cancellationToken = default);

    /// <summary>用户修改自己的密码（需校验原密码）。</summary>
    Task ChangeOwnPasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
