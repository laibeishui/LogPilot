using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LogPilot.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Storage:Sqlite:DataSource",
                Path.Combine(Path.GetTempPath(), $"logpilot-health-{Guid.NewGuid():N}.db"));
            builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
        });
    }

    [Fact]
    public async Task Live_ReturnsOk()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_RequiresAuth_WhenInitializedOrRedirectsToSetup()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync("/");
        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.Unauthorized
            || (int)response.StatusCode is >= 300 and < 400);
    }
}
