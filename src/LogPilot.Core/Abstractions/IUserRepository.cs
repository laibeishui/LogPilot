using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

public interface IUserRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    Task<long> InsertAsync(UserAccount user, CancellationToken cancellationToken = default);

    Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken = default);
}
