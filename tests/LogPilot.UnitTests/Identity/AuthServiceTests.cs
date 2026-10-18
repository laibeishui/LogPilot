using LogPilot.Application.Common;
using LogPilot.Application.Identity;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Xunit;

namespace LogPilot.UnitTests.Identity;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task SetupAdmin_CreatesOnce_ThenRejects()
    {
        var users = new InMemoryUsers();
        var hasher = new FakeHasher();
        var service = new AuthService(users, hasher);

        Assert.True(await service.NeedsSetupAsync());
        var admin = await service.SetupAdminAsync(new SetupAdminRequest
        {
            Username = "admin",
            Password = "password123"
        });

        Assert.Equal("admin", admin.Username);
        Assert.True(admin.IsSystemAdmin);
        Assert.False(await service.NeedsSetupAsync());

        var ex = await Assert.ThrowsAsync<AppException>(() => service.SetupAdminAsync(new SetupAdminRequest
        {
            Username = "admin2",
            Password = "password123"
        }));
        Assert.Equal("ALREADY_INITIALIZED", ex.Code);
    }

    [Fact]
    public async Task Login_WithWrongPassword_UsesUniformError()
    {
        var users = new InMemoryUsers();
        var hasher = new FakeHasher();
        var service = new AuthService(users, hasher);
        await service.SetupAdminAsync(new SetupAdminRequest
        {
            Username = "admin",
            Password = "password123"
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => service.ValidateLoginAsync(new LoginRequest
        {
            Username = "admin",
            Password = "wrong-password"
        }));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("UNAUTHORIZED", ex.Code);
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string HashPassword(string password) => "hash:" + password;

        public bool VerifyPassword(string hashedPassword, string providedPassword)
            => hashedPassword == "hash:" + providedPassword;
    }

    private sealed class InMemoryUsers : IUserRepository
    {
        private readonly List<UserAccount> _users = new();
        private long _seq = 1;

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Count);

        public Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Count(x => x.IsSystemAdmin && x.Status == EntityStatus.Enabled));

        public Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(x => x.Id == id));

        public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(x =>
                string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));

        public Task<long> InsertAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            var id = _seq++;
            _users.Add(new UserAccount
            {
                Id = id,
                Username = user.Username,
                DisplayName = user.DisplayName,
                PasswordHash = user.PasswordHash,
                Status = user.Status,
                IsSystemAdmin = user.IsSystemAdmin,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                LastLoginAt = user.LastLoginAt
            });
            return Task.FromResult(id);
        }

        public Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(IReadOnlyList<UserAccount> Items, int Total)> ListAsync(
            string? keyword, int offset, int limit, CancellationToken cancellationToken = default)
        {
            var query = _users.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x =>
                    x.Username.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || x.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var all = query.OrderBy(x => x.Id).ToList();
            return Task.FromResult<(IReadOnlyList<UserAccount>, int)>(
                (all.Skip(offset).Take(limit).ToList(), all.Count));
        }

        public Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
        {
            var index = _users.FindIndex(x => x.Id == user.Id);
            if (index >= 0)
            {
                _users[index] = user;
            }

            return Task.CompletedTask;
        }

        public Task<bool> UsernameExistsAsync(string username, long? excludeUserId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Any(x =>
                x.Id != excludeUserId
                && string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase)));
    }
}
