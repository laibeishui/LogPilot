using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LogPilot.IntegrationTests;

public sealed class AuthFlowTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthFlowTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Storage:Sqlite:DataSource",
                Path.Combine(Path.GetTempPath(), $"logpilot-auth-{Guid.NewGuid():N}.db"));
            builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
        });
    }

    [Fact]
    public async Task Setup_Login_CreateProject_AndApiKey()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        // CSRF
        var tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        var csrf = tokenResponse.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(csrf));

        // Setup admin
        using (var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/setup/admin"))
        {
            setupRequest.Headers.Add("X-CSRF-TOKEN", csrf);
            setupRequest.Content = JsonContent.Create(new
            {
                username = "admin",
                password = "password123",
                displayName = "Admin"
            });
            var setupResponse = await client.SendAsync(setupRequest);
            Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        }

        // Refresh CSRF after cookie auth established
        tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        csrf = tokenResponse.GetProperty("token").GetString();

        // Create project
        long projectId;
        using (var projectRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/projects"))
        {
            projectRequest.Headers.Add("X-CSRF-TOKEN", csrf!);
            projectRequest.Content = JsonContent.Create(new
            {
                name = "HiLink",
                code = "hilink",
                description = "demo"
            });
            var projectResponse = await client.SendAsync(projectRequest);
            Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
            var project = await projectResponse.Content.ReadFromJsonAsync<JsonElement>();
            projectId = project.GetProperty("id").GetInt64();
            Assert.True(projectId > 0);
        }

        tokenResponse = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery/token");
        csrf = tokenResponse.GetProperty("token").GetString();

        // Create API key (plaintext once)
        using (var keyRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/projects/{projectId}/api-keys"))
        {
            keyRequest.Headers.Add("X-CSRF-TOKEN", csrf!);
            keyRequest.Content = JsonContent.Create(new { name = "prod", expiresAt = (string?)null });
            var keyResponse = await client.SendAsync(keyRequest);
            Assert.Equal(HttpStatusCode.OK, keyResponse.StatusCode);
            var key = await keyResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.StartsWith("lp_", key.GetProperty("apiKey").GetString());
            Assert.Equal("enabled", key.GetProperty("status").GetString());
        }

        // List keys should not include plaintext
        var keys = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/projects/{projectId}/api-keys");
        Assert.NotNull(keys);
        Assert.Single(keys!);
        Assert.False(keys![0].TryGetProperty("apiKey", out var apiKeyProp) && apiKeyProp.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(apiKeyProp.GetString())
            && apiKeyProp.GetString()!.StartsWith("lp_")
            && apiKeyProp.GetString()!.Length > 20);
    }
}
