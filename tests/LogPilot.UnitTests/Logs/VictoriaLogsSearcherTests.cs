using System.Net;
using System.Text;
using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.VictoriaLogs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LogPilot.UnitTests.Logs;

/// <summary>
/// 日志存储故障必须以异常上报：一旦降级成 0 条/空结果，页面会显示“没有日志”，
/// 告警评估还会把存储故障误判为告警恢复。
/// </summary>
public sealed class VictoriaLogsSearcherTests
{
    private static readonly DateTimeOffset End = DateTimeOffset.Parse("2026-10-18T10:00:00Z");

    [Fact]
    public async Task CountAsync_ThrowsStoreUnavailable_WhenVictoriaLogsReturns500()
    {
        var searcher = CreateSearcher(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.CountAsync(
            1, End.AddHours(-1), End, null, null, Array.Empty<string>(), null, null));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
        Assert.Equal(503, ex.StatusCode);
    }

    [Fact]
    public async Task CountAsync_ThrowsStoreUnavailable_WhenVictoriaLogsIsUnreachable()
    {
        var searcher = CreateSearcher(new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.CountAsync(
            1, End.AddHours(-1), End, null, null, Array.Empty<string>(), null, null));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
    }

    [Fact]
    public async Task SearchAsync_ThrowsStoreUnavailable_WhenTotalCountFails()
    {
        var searcher = CreateSearcher(HttpStatusCode.ServiceUnavailable, "down");

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.SearchAsync(new AuthorizedLogQuery
        {
            ProjectId = 1,
            StartTime = End.AddHours(-1),
            EndTime = End
        }));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
    }

    [Fact]
    public async Task ListServicesAsync_ThrowsStoreUnavailable_WhenQueryFails()
    {
        var searcher = CreateSearcher(HttpStatusCode.BadGateway, "bad gateway");

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.ListServicesAsync(
            1, End.AddHours(-1), End));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
    }

    [Fact]
    public async Task TrendAsync_ThrowsStoreUnavailable_WhenQueryFails()
    {
        var searcher = CreateSearcher(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.TrendAsync(
            1, End.AddHours(-1), End, "5m", false));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
    }

    [Fact]
    public async Task ListErrorSamplesAsync_ThrowsStoreUnavailable_WhenQueryFails()
    {
        var searcher = CreateSearcher(HttpStatusCode.InternalServerError, "boom");

        var ex = await Assert.ThrowsAsync<AppException>(() => searcher.ListErrorSamplesAsync(
            1, End.AddHours(-1), End, 100));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
    }

    [Fact]
    public async Task CountAsync_ReturnsParsedCount_WhenQuerySucceeds()
    {
        // VictoriaLogs 的 count 可能是字符串，必须仍然能解析
        var searcher = CreateSearcher(HttpStatusCode.OK, "{\"count(*)\":\"12\"}");

        var count = await searcher.CountAsync(
            1, End.AddHours(-1), End, null, null, Array.Empty<string>(), null, null);

        Assert.Equal(12, count);
    }

    [Fact]
    public async Task CountAsync_ReturnsZero_WhenStoreHasNoData()
    {
        var searcher = CreateSearcher(HttpStatusCode.OK, string.Empty);

        var count = await searcher.CountAsync(
            1, End.AddHours(-1), End, null, null, Array.Empty<string>(), null, null);

        Assert.Equal(0, count);
    }

    private static VictoriaLogsSearcher CreateSearcher(HttpStatusCode status, string body)
        => new(
            new StubHttpClientFactory(new StubHandler(status, body)),
            NullLogger<VictoriaLogsSearcher>.Instance);

    private static VictoriaLogsSearcher CreateSearcher(Exception failure)
        => new(
            new StubHttpClientFactory(new StubHandler(failure)),
            NullLogger<VictoriaLogsSearcher>.Instance);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://victorialogs.test/")
            };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _failure;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public StubHandler(Exception failure) => _failure = failure;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_failure is not null)
            {
                return Task.FromException<HttpResponseMessage>(_failure);
            }

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
