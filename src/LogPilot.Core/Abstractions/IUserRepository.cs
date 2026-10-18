using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IUserRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分页检索用户；<paramref name="keyword"/> 同时匹配用户名与显示名。
    /// </summary>
    Task<(IReadOnlyList<UserAccount> Items, int Total)> ListAsync(
        string? keyword,
        int offset,
        int limit,
        CancellationToken cancellationToken = default);

    Task<long> InsertAsync(UserAccount user, CancellationToken cancellationToken = default);

    /// <summary>更新显示名、状态、系统管理员标志与密码哈希。</summary>
    Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default);

    Task<bool> UsernameExistsAsync(
        string username,
        long? excludeUserId = null,
        CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken = default);
}
