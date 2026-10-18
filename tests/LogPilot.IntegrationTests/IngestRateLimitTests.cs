using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LogPilot.IntegrationTests;

/// <summary>
/// 接入端点限流：超过每分钟配额必须返回 429（限流在鉴权之前生效），非接入端点不受影响。
/// </summary>
public sealed class IngestRateLimitTests
{
    [Fact]
    public async Task OtlpIngest_Returns429_AfterExceedingPerMinuteQuota()
    {
        await using var factory = CreateFactory(perMinute: 3);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/logs")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        // 前 3 次在配额内（未带接入密钥 → 401），第 4 次被限流
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[1]);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[2]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[3]);
    }

    [Fact]
    public async Task JsonIngest_Returns429_AfterExceedingPerMinuteQuota()
    {
        await using var factory = CreateFactory(perMinute: 2);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs")
            {
                Content = new StringContent("{\"logs\":[]}", Encoding.UTF8, "application/json")
            };
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[1]);
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[2]);
    }

    [Fact]
    public async Task NonIngestEndpoint_IsNotRateLimited()
    {
        await using var factory = CreateFactory(perMinute: 1);
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var response = await client.GetAsync("/api/v1/antiforgery/token");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(int perMinute)
        => new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Storage:Sqlite:DataSource",
                    Path.Combine(Path.GetTempPath(), $"logpilot-ratelimit-{Guid.NewGuid():N}.db"));
                builder.UseSetting("Storage:VictoriaLogs:BaseUrl", "http://127.0.0.1:1");
                builder.UseSetting("Ingestion:MaxRequestsPerMinute", perMinute.ToString());
            });
}
