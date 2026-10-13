using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LogPilot.Core.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LogPilot.IntegrationTests;

public sealed class LogSearchApiTests
{
    [Fact]
    public async Task Search_RequiresAuthentication()
    {
        await using var factory = CreateFactory(new FakeSearcher());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // 先完成初始化，再退出登录，验证管理查询必须带会话
        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;
        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/admin"))
        {
            setup.Headers.Add("X-CSRF-TOKEN", token);
            setup.Content = JsonContent.Create(new { username = "admin", password = "password123" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(setup)).StatusCode);
        }

        token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;
        using (var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout"))
        {
            logout.Headers.Add("X-CSRF-TOKEN", token);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(logout)).StatusCode);
        }

        token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;
        using var search = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logs/search");
        search.Headers.Add("X-CSRF-TOKEN", token);
        search.Content = JsonContent.Create(new { projectId = 1 });
        var response = await client.SendAsync(search);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_ReturnsItems_ForAuthorizedUser()
    {
        await using var factory = CreateFactory(new FakeSearcher());
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;

        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/admin"))
        {
            setup.Headers.Add("X-CSRF-TOKEN", token);
            setup.Content = JsonContent.Create(new { username = "admin", password = "password123" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(setup)).StatusCode);
        }

        token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;

        long projectId;
        using (var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects"))
        {
            create.Headers.Add("X-CSRF-TOKEN", token);
            create.Content = JsonContent.Create(new { name = "Demo", code = "demo" });
            var created = await client.SendAsync(create);
            created.EnsureSuccessStatusCode();
            projectId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        }

        token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token"))
            .GetProperty("token").GetString()!;

        using var search = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logs/search");
        search.Headers.Add("X-CSRF-TOKEN", token);
        search.Content = JsonContent.Create(new
        {
            projectId,
            pageSize = 20
        });

        var response = await client.SendAsync(search);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("items").GetArrayLength());
        Assert.Equal("evt_demo", body.GetProperty("items")[0].GetProperty("id").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(ILogSearcher searcher)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Storage:Sqlite:DataSource",
                Path.Combine(Path.GetTempPath(), $"logpilot-search-{Guid.NewGuid():N}.db"));
            builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILogSearcher>();
                services.AddSingleton(searcher);
            });
        });

    private sealed class FakeSearcher : ILogSearcher
    {
        public Task<LogSearchResult> SearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new LogSearchResult
            {
                Items =
                [
                    new LogSearchItem
                    {
                        Id = "evt_demo",
                        Timestamp = DateTimeOffset.UtcNow,
                        ServiceName = "demo-api",
                        Level = "INFO",
                        Message = "hello"
                    }
                ],
                HasMore = false
            });

        public Task<LogDetail?> GetByEventIdAsync(
            long projectId,
            string eventId,
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            CancellationToken cancellationToken = default)
            => Task.FromResult<LogDetail?>(new LogDetail
            {
                Id = eventId,
                ProjectId = projectId,
                Timestamp = DateTimeOffset.UtcNow,
                ServiceName = "demo-api",
                Level = "INFO",
                Message = "hello"
            });

        public Task<IReadOnlyList<string>> ListServicesAsync(
            long projectId,
            DateTimeOffset startTime,
            DateTimeOffset endTime,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["demo-api"]);
    }
}
