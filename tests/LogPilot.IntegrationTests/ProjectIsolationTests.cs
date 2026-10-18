using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LogPilot.IntegrationTests;

/// <summary>
/// 人员管理 + 项目成员 + 按项目隔离日志可见性。
/// </summary>
public sealed class ProjectIsolationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProjectIsolationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Storage:Sqlite:DataSource",
                Path.Combine(Path.GetTempPath(), $"logpilot-isolation-{Guid.NewGuid():N}.db"));
            builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
        });
    }

    [Fact]
    public async Task Admin_ManagesUsersAndMemberships_PlainUserSeesOnlyAssignedProjects()
    {
        var admin = NewClient();
        await SetupAdminAsync(admin);

        var projectA = await CreateProjectAsync(admin, "alpha", "Alpha");
        var projectB = await CreateProjectAsync(admin, "beta", "Beta");

        // 普通用户：建号后默认无项目、无管理权限
        var userId = await CreateUserAsync(admin, "zhangsan");
        var user = NewClient();
        await LoginAsync(user, "zhangsan", "password123");

        Assert.False(await HasAdminAccessAsync(user));
        Assert.Empty(await GetProjectsAsync(user));

        // 授权项目 A（viewer）
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/users/{userId}/memberships"))
        {
            request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(admin));
            request.Content = JsonContent.Create(new
            {
                memberships = new[] { new { projectId = projectA, role = "Viewer" } }
            });
            var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var visible = await GetProjectsAsync(user);
        var only = Assert.Single(visible);
        Assert.Equal(projectA, only.GetProperty("id").GetInt64());
        Assert.Equal(1, only.GetProperty("memberCount").GetInt32());

        // 未授权项目 B 的密钥接口必须 403
        var forbidden = await user.GetAsync($"/api/v1/projects/{projectB}/api-keys");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // 项目视角成员列表
        var members = await admin.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectA}/members");
        var member = Assert.Single(members!);
        Assert.Equal("zhangsan", member.GetProperty("username").GetString());
        Assert.Equal("viewer", member.GetProperty("role").GetString());

        // 移除成员后立即不可见
        using (var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/projects/{projectA}/members/{userId}"))
        {
            request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(admin));
            var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(await GetProjectsAsync(user));
    }

    [Fact]
    public async Task PlainUser_CannotCreateProjectOrReachUserManagement()
    {
        var admin = NewClient();
        await SetupAdminAsync(admin);
        await CreateUserAsync(admin, "zhangsan");

        var user = NewClient();
        await LoginAsync(user, "zhangsan", "password123");

        Assert.False(await HasAdminAccessAsync(user));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(user));
        request.Content = JsonContent.Create(new { name = "Hack", code = "hack" });
        var response = await user.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tail_WithoutProjectMembership_IsRejectedBeforeStreaming()
    {
        var admin = NewClient();
        await SetupAdminAsync(admin);
        var projectId = await CreateProjectAsync(admin, "alpha", "Alpha");
        await CreateUserAsync(admin, "zhangsan");

        var user = NewClient();
        await LoginAsync(user, "zhangsan", "password123");

        // 建连即应被拒：必须返回非 200，否则 EventSource 会陷入重连循环
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await user.GetAsync($"/api/v1/logs/tail?projectId={projectId}", cts.Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DisabledAccount_LosesSessionImmediately()
    {
        var admin = NewClient();
        await SetupAdminAsync(admin);
        var userId = await CreateUserAsync(admin, "zhangsan");

        var user = NewClient();
        await LoginAsync(user, "zhangsan", "password123");
        Assert.False(await HasAdminAccessAsync(user));

        using (var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/users/{userId}"))
        {
            request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(admin));
            request.Content = JsonContent.Create(new { enabled = false });
            var response = await admin.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var response2 = await user.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
    }

    private HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        return token.GetProperty("token").GetString()!;
    }

    private static async Task SetupAdminAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/admin");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new
        {
            username = "admin",
            password = "password123",
            displayName = "Admin"
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task LoginAsync(HttpClient client, string username, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new { username, password });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<long> CreateProjectAsync(HttpClient client, string code, string name)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new { name, code });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, project.GetProperty("memberCount").GetInt32());
        return project.GetProperty("id").GetInt64();
    }

    private static async Task<long> CreateUserAsync(HttpClient client, string username)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new
        {
            username,
            password = "password123",
            displayName = username,
            isSystemAdmin = false
        });

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<JsonElement>();
        return user.GetProperty("id").GetInt64();
    }

    private static async Task<bool> HasAdminAccessAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/users");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private static async Task<JsonElement[]> GetProjectsAsync(HttpClient client)
    {
        var projects = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/projects");
        return projects ?? [];
    }
}
