using LogPilot.Application.Common;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Xunit;

namespace LogPilot.UnitTests.Projects;

public sealed class ProjectMemberServiceTests
{
    [Fact]
    public async Task ReplaceByUserAsync_RejectsUnknownUser()
    {
        var (service, _, _) = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() => service.ReplaceByUserAsync(
            404, new UserMembershipsRequest { Memberships = [] }));

        Assert.Equal("NOT_FOUND", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ReplaceByUserAsync_RejectsUnknownProject()
    {
        var (service, _, users) = CreateService();
        var userId = await SeedUserAsync(users);

        var ex = await Assert.ThrowsAsync<AppException>(() => service.ReplaceByUserAsync(
            userId, new UserMembershipsRequest
            {
                Memberships = [new UserMembershipInput { ProjectId = 999, Role = ProjectMemberRole.Viewer }]
            }));

        Assert.Equal("NOT_FOUND", ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task ReplaceByUserAsync_RejectsDuplicateProject()
    {
        var (service, projects, users) = CreateService();
        var userId = await SeedUserAsync(users);
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });

        var ex = await Assert.ThrowsAsync<AppException>(() => service.ReplaceByUserAsync(
            userId, new UserMembershipsRequest
            {
                Memberships =
                [
                    new UserMembershipInput { ProjectId = 1, Role = ProjectMemberRole.Viewer },
                    new UserMembershipInput { ProjectId = 1, Role = ProjectMemberRole.Admin }
                ]
            }));

        Assert.Equal("INVALID_ARGUMENT", ex.Code);
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ReplaceByUserAsync_ReplacesExistingAssignments()
    {
        var (service, projects, users) = CreateService();
        var userId = await SeedUserAsync(users, "zhangsan");
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });
        projects.Add(new Project { Id = 2, Code = "beta", Name = "Beta" });

        await service.ReplaceByUserAsync(userId, new UserMembershipsRequest
        {
            Memberships = [new UserMembershipInput { ProjectId = 1, Role = ProjectMemberRole.Viewer }]
        });

        var result = await service.ReplaceByUserAsync(userId, new UserMembershipsRequest
        {
            Memberships = [new UserMembershipInput { ProjectId = 2, Role = ProjectMemberRole.Owner }]
        });

        var single = Assert.Single(result);
        Assert.Equal(2, single.ProjectId);
        Assert.Equal("Beta", single.ProjectName);
        Assert.Equal("owner", single.Role);
    }

    [Fact]
    public async Task ReplaceByUserAsync_WithoutMemberships_RevokesAll()
    {
        var (service, projects, users) = CreateService();
        var userId = await SeedUserAsync(users);
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });
        await service.ReplaceByUserAsync(userId, new UserMembershipsRequest
        {
            Memberships = [new UserMembershipInput { ProjectId = 1, Role = ProjectMemberRole.Admin }]
        });

        var result = await service.ReplaceByUserAsync(userId, new UserMembershipsRequest());

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByProjectAsync_RejectsUnknownProject()
    {
        var (service, _, _) = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() => service.ListByProjectAsync(123));

        Assert.Equal("NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task AddOrUpdateAsync_UpsertsRole_AndReturnsProjectMembers()
    {
        var (service, projects, users) = CreateService();
        var userId = await SeedUserAsync(users, "zhangsan");
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });

        var created = await service.AddOrUpdateAsync(1, new AddProjectMemberRequest
        {
            UserId = userId,
            Role = ProjectMemberRole.Viewer
        });
        Assert.Equal("viewer", Assert.Single(created).Role);

        var updated = await service.AddOrUpdateAsync(1, new AddProjectMemberRequest
        {
            UserId = userId,
            Role = ProjectMemberRole.Owner
        });

        var member = Assert.Single(updated);
        Assert.Equal("owner", member.Role);
        Assert.Equal("zhangsan", member.Username);
    }

    [Fact]
    public async Task AddOrUpdateAsync_RejectsUnknownUser()
    {
        var (service, projects, _) = CreateService();
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });

        var ex = await Assert.ThrowsAsync<AppException>(() => service.AddOrUpdateAsync(
            1, new AddProjectMemberRequest { UserId = 404, Role = ProjectMemberRole.Viewer }));

        Assert.Equal("NOT_FOUND", ex.Code);
    }

    [Fact]
    public async Task RemoveAsync_DropsMembership()
    {
        var (service, projects, users) = CreateService();
        var userId = await SeedUserAsync(users);
        projects.Add(new Project { Id = 1, Code = "alpha", Name = "Alpha" });
        await service.AddOrUpdateAsync(1, new AddProjectMemberRequest
        {
            UserId = userId,
            Role = ProjectMemberRole.Admin
        });

        var remaining = await service.RemoveAsync(1, userId);

        Assert.Empty(remaining);
    }

    private static (ProjectMemberService Service, FakeProjects Projects, FakeUsers Users) CreateService()
    {
        var users = new FakeUsers();
        var projects = new FakeProjects(users);
        return (new ProjectMemberService(projects, users), projects, users);
    }

    private static async Task<long> SeedUserAsync(FakeUsers users, string username = "viewer1")
    {
        var now = DateTimeOffset.UtcNow;
        return await users.InsertAsync(new UserAccount
        {
            Username = username,
            DisplayName = username,
            PasswordHash = "hash:password123",
            Status = EntityStatus.Enabled,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private sealed class FakeProjects : IProjectRepository
    {
        private readonly FakeUsers _users;
        private readonly Dictionary<long, Project> _projects = new();
        private readonly List<(long ProjectId, long UserId, ProjectMemberRole Role, DateTimeOffset CreatedAt)> _members = new();

        public FakeProjects(FakeUsers users) => _users = users;

        public void Add(Project project) => _projects[project.Id] = project;

        public Task<Project?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_projects.TryGetValue(id, out var project) ? project : null);

        public async Task<IReadOnlyList<ProjectMember>> ListMembersAsync(long projectId, CancellationToken cancellationToken = default)
        {
            var result = new List<ProjectMember>();
            foreach (var m in _members.Where(x => x.ProjectId == projectId))
            {
                var user = await _users.FindByIdAsync(m.UserId, cancellationToken);
                result.Add(new ProjectMember
                {
                    ProjectId = m.ProjectId,
                    UserId = m.UserId,
                    Username = user?.Username ?? string.Empty,
                    DisplayName = user?.DisplayName ?? string.Empty,
                    Role = m.Role,
                    CreatedAt = m.CreatedAt
                });
            }

            return result.OrderByDescending(x => x.Role).ToList();
        }

        public Task UpsertMemberAsync(long projectId, long userId, ProjectMemberRole role, CancellationToken cancellationToken = default)
        {
            _members.RemoveAll(x => x.ProjectId == projectId && x.UserId == userId);
            _members.Add((projectId, userId, role, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default)
        {
            _members.RemoveAll(x => x.ProjectId == projectId && x.UserId == userId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProjectMembership>> ListMembershipsByUserAsync(long userId, CancellationToken cancellationToken = default)
        {
            var result = _members
                .Where(x => x.UserId == userId && _projects.ContainsKey(x.ProjectId))
                .Select(x => new ProjectMembership
                {
                    ProjectId = x.ProjectId,
                    ProjectCode = _projects[x.ProjectId].Code,
                    ProjectName = _projects[x.ProjectId].Name,
                    Role = x.Role
                })
                .ToList();
            return Task.FromResult<IReadOnlyList<ProjectMembership>>(result);
        }

        public Task ReplaceMembershipsAsync(
            long userId,
            IReadOnlyList<(long ProjectId, ProjectMemberRole Role)> memberships,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            _members.RemoveAll(x => x.UserId == userId);
            foreach (var (projectId, role) in memberships)
            {
                _members.Add((projectId, userId, role, updatedAt));
            }

            return Task.CompletedTask;
        }

        public Task<Project?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Project>> ListAccessibleAsync(long userId, bool isSystemAdmin, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<long> InsertAsync(Project project, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task AddMemberAsync(long projectId, long userId, ProjectMemberRole role, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ProjectMemberRole?> GetMemberRoleAsync(long projectId, long userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<long, int>> CountMembersByProjectAsync(
            IReadOnlyList<long> projectIds,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeUsers : IUserRepository
    {
        private readonly List<UserAccount> _users = new();
        private long _seq = 1;

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Count);

        public Task<int> CountSystemAdminsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<UserAccount?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(x => x.Id == id));

        public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(x => x.Username == username));

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
                UpdatedAt = user.UpdatedAt
            });
            return Task.FromResult(id);
        }

        public Task UpdateLastLoginAsync(long userId, DateTimeOffset lastLoginAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(IReadOnlyList<UserAccount> Items, int Total)> ListAsync(
            string? keyword, int offset, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(UserAccount user, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> UsernameExistsAsync(string username, long? excludeUserId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.Any(x => x.Username == username));
    }
}
