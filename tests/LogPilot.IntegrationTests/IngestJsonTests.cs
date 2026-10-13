using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LogPilot.IntegrationTests;

public sealed class IngestJsonTests
{
    [Fact]
    public async Task JsonIngest_WithValidApiKey_AcceptsLogs()
    {
        var writer = new CapturingWriter();
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Storage:Sqlite:DataSource",
                    Path.Combine(Path.GetTempPath(), $"logpilot-ingest-{Guid.NewGuid():N}.db"));
                builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ILogWriter>();
                    services.AddSingleton<ILogWriter>(writer);
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        var csrf = tokenResponse.GetProperty("token").GetString()!;

        using (var setup = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/admin"))
        {
            setup.Headers.Add("X-CSRF-TOKEN", csrf);
            setup.Content = JsonContent.Create(new { username = "admin", password = "password123" });
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(setup)).StatusCode);
        }

        tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        csrf = tokenResponse.GetProperty("token").GetString()!;

        long projectId;
        using (var createProject = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects"))
        {
            createProject.Headers.Add("X-CSRF-TOKEN", csrf);
            createProject.Content = JsonContent.Create(new { name = "Demo", code = "demo", description = "" });
            var projectResponse = await client.SendAsync(createProject);
            projectResponse.EnsureSuccessStatusCode();
            projectId = (await projectResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        }

        tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        csrf = tokenResponse.GetProperty("token").GetString()!;

        string apiKey;
        using (var createKey = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/api-keys"))
        {
            createKey.Headers.Add("X-CSRF-TOKEN", csrf);
            createKey.Content = JsonContent.Create(new { name = "ingest" });
            var keyResponse = await client.SendAsync(createKey);
            keyResponse.EnsureSuccessStatusCode();
            apiKey = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString()!;
        }

        using var ingest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        ingest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        ingest.Content = JsonContent.Create(new
        {
            logs = new[]
            {
                new
                {
                    timestamp = "2026-10-13T03:00:00Z",
                    serviceName = "payment-api",
                    level = "Error",
                    message = "Database connection timeout",
                    projectId = 999,
                    attributes = new { module = "Payments" }
                }
            }
        });

        var ingestResponse = await client.SendAsync(ingest);
        Assert.Equal(HttpStatusCode.OK, ingestResponse.StatusCode);
        var body = await ingestResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("accepted").GetInt32());
        Assert.Equal("accepted", body.GetProperty("status").GetString());

        Assert.Single(writer.Logs);
        Assert.Equal(projectId, writer.Logs[0].ProjectId);
        Assert.Equal("payment-api", writer.Logs[0].ServiceName);
        Assert.Equal("ERROR", writer.Logs[0].Level);
    }

    [Fact]
    public async Task JsonIngest_WithBadToken_ReturnsUnauthorized()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Storage:Sqlite:DataSource",
                    Path.Combine(Path.GetTempPath(), $"logpilot-ingest-bad-{Guid.NewGuid():N}.db"));
                builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
            });

        var client = factory.CreateClient();
        using var ingest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        ingest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "lp_invalid");
        ingest.Content = JsonContent.Create(new
        {
            logs = new[] { new { serviceName = "x", message = "y", level = "Info" } }
        });

        var response = await client.SendAsync(ingest);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class CapturingWriter : ILogWriter
    {
        public List<NormalizedLog> Logs { get; } = new();

        public Task<LogWriteResult> WriteAsync(
            IReadOnlyList<NormalizedLog> logs,
            CancellationToken cancellationToken = default)
        {
            Logs.AddRange(logs);
            return Task.FromResult(LogWriteResult.Success(logs.Count));
        }
    }
}
