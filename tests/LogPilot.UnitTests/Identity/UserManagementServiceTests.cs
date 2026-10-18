using LogPilot.Application.Common;
using LogPilot.Application.Identity;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Xunit;

namespace LogPilot.UnitTests.Identity;

public sealed class UserManagementServiceTests
{
    private static (UserManagementService Service, InMemoryUsers Users) CreateService()
    {
        var users = new InMemoryUsers();
        return (new UserManagementService(users, new FakeHasher()), users);
    }

    [Fact]
    public async Task CreateAsync_TrimsUsername_AndDefaultsToEnabledNonAdmin()
    {
        var (service, _) = CreateService();

        var dto = await service.CreateAsync(new CreateUserRequest
        {
            Username = "  zhangsan  ",
            Password = "password123"
        });

        Assert.Equal("zhangsan", dto.Username);
        Assert.Equal("zhangsan", dto.DisplayName);
        Assert.Equal("enabled", dto.Status);
        Assert.False(dto.IsSystemAdmin);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("bad/name")]
    public async Task CreateAsync_RejectsInvalidUsername(string username)
    {
        var (service, _) = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateAsync(new CreateUserRequest { Username = username, Password = "password123" }));

        Assert.Equal("INVALID_ARGUMENT", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task CreateAsync_RejectsShortPassword()
    {
        var (service, _) = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateAsync(new CreateUserRequest { Username = "zhangsan", Password = "short" }));

        Assert.Equal("INVALID_ARGUMENT", ex.Code);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateUsername_IgnoringCase()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new CreateUserRequest { Username = "zhangsan", Password = "password123" });

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.CreateAsync(new CreateUserRequest { Username = "ZhangSan", Password = "password123" }));

        Assert.Equal("CONFLICT", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task ListAsync_ClampsPageSize_AndCountsTotal()
    {
        var (service, users) = CreateService();
        for (var i = 0; i < 3; i++)
        {
            await users.InsertAsync(new UserAccount
            {
                Username = $"user{i}",
                DisplayName = $"用户{i}",
                PasswordHash = "hash:x",
                Status = EntityStatus.Enabled,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        var result = await service.ListAsync(null, 0, 0);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(3, result.Items.Count);
    }

    [Fact]
    public async Task ListAsync_FiltersByKeyword()
    {
        var (service, users) = CreateService();
        await users.InsertAsync(new UserAccount
        {
            Username = "zhangsan",
            DisplayName = "张三",
            PasswordHash = "hash:x",
            Status = EntityStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await users.InsertAsync(new UserAccount
        {
            Username = "lisi",
            DisplayName = "李四",
            PasswordHash = "hash:x",
            Status = EntityStatus.Enabled,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await service.ListAsync("李四", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Equal("lisi", result.Items[0].Username);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDisablingSelf()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateAsync(id, id, new UpdateUserRequest { Enabled = false }));

        Assert.Equal("CONFLICT", ex.Code);
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateAsync_RejectsRemovingLastEnabledSystemAdmin()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateAsync(operatorUserId: 999, id, new UpdateUserRequest { IsSystemAdmin = false }));

        Assert.Equal("CONFLICT", ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDisablingLastEnabledSystemAdmin()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateAsync(operatorUserId: 999, id, new UpdateUserRequest { Enabled = false }));

        Assert.Equal("CONFLICT", ex.Code);
    }

    [Fact]
    public async Task UpdateAsync_AllowsDemotingAdmin_WhenAnotherEnabledAdminExists()
    {
        var (service, users) = CreateService();
        var first = await SeedAdminAsync(users, "admin1");
        await SeedAdminAsync(users, "admin2");

        var dto = await service.UpdateAsync(operatorUserId: first, first, new UpdateUserRequest { IsSystemAdmin = false });

        Assert.False(dto.IsSystemAdmin);
    }

    [Fact]
    public async Task UpdateAsync_AllowsDisablingAdmin_WhenAnotherEnabledAdminExists()
    {
        var (service, users) = CreateService();
        var first = await SeedAdminAsync(users, "admin1");
        var second = await SeedAdminAsync(users, "admin2");

        var dto = await service.UpdateAsync(operatorUserId: second, first, new UpdateUserRequest { Enabled = false });

        Assert.Equal("disabled", dto.Status);
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnknownUser()
    {
        var (service, _) = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.UpdateAsync(1, 404, new UpdateUserRequest { Enabled = false }));

        Assert.Equal("NOT_FOUND", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_RejectsWrongCurrentPassword()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.ChangeOwnPasswordAsync(id, new ChangePasswordRequest
            {
                CurrentPassword = "wrong-password",
                NewPassword = "newpass123"
            }));

        Assert.Equal("INVALID_CREDENTIALS", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ChangeOwnPasswordAsync_UpdatesHash()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        await service.ChangeOwnPasswordAsync(id, new ChangePasswordRequest
        {
            CurrentPassword = "password123",
            NewPassword = "newpass123"
        });

        var saved = await users.FindByIdAsync(id);
        Assert.Equal("hash:newpass123", saved!.PasswordHash);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReplacesHash_WithoutTouchingOtherFields()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        await service.ResetPasswordAsync(id, new ResetPasswordRequest { Password = "resetpass123" });

        var saved = await users.FindByIdAsync(id);
        Assert.Equal("hash:resetpass123", saved!.PasswordHash);
        Assert.True(saved.IsSystemAdmin);
        Assert.Equal(EntityStatus.Enabled, saved.Status);
    }

    [Fact]
    public async Task ResetPasswordAsync_RejectsShortPassword()
    {
        var (service, users) = CreateService();
        var id = await SeedAdminAsync(users, "admin");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            service.ResetPasswordAsync(id, new ResetPasswordRequest { Password = "short" }));

        Assert.Equal("INVALID_ARGUMENT", ex.Code);
    }

    private static async Task<long> SeedAdminAsync(InMemoryUsers users, string username)
    {
        var now = DateTimeOffset.UtcNow;
        return await users.InsertAsync(new UserAccount
        {
            Username = username,
            DisplayName = username,
            PasswordHash = "hash:password123",
            Status = EntityStatus.Enabled,
            IsSystemAdmin = true,
            CreatedAt = now,
            UpdatedAt = now
        });
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
