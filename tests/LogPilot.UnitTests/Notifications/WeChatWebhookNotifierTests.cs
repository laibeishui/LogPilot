using System.Net;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using LogPilot.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LogPilot.UnitTests.Notifications;

/// <summary>
/// 钉钉/企微在 HTTP 200 时仍可能返回业务错误（如加签失败、关键词不匹配），
/// 必须解析响应体 errcode，否则会把发送失败当成成功，事件被错误标记为已通知。
/// </summary>
public sealed class WeChatWebhookNotifierTests
{
    [Fact]
    public async Task DingTalk_ReturnsFailure_WhenErrcodeIsNotZero()
    {
        var notifier = new DingTalkNotifier(
            new StubHttpClientFactory(HttpStatusCode.OK, "{\"errcode\":310000,\"errmsg\":\"keywords not in content\"}"),
            NullLogger<DingTalkNotifier>.Instance);

        var result = await notifier.SendAsync(CreateNotification(), "{\"webhookUrl\":\"https://example.com/hook\"}");

        Assert.False(result.Succeeded);
        Assert.Contains("310000", result.ErrorMessage);
        Assert.Contains("keywords not in content", result.ErrorMessage);
    }

    [Fact]
    public async Task DingTalk_ReturnsSuccess_WhenErrcodeIsZero()
    {
        var notifier = new DingTalkNotifier(
            new StubHttpClientFactory(HttpStatusCode.OK, "{\"errcode\":0,\"errmsg\":\"ok\"}"),
            NullLogger<DingTalkNotifier>.Instance);

        var result = await notifier.SendAsync(CreateNotification(), "{\"webhookUrl\":\"https://example.com/hook\"}");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task WeCom_ReturnsFailure_WhenErrcodeIsNotZero()
    {
        var notifier = new WeComNotifier(
            new StubHttpClientFactory(HttpStatusCode.OK, "{\"errcode\":93000,\"errmsg\":\"invalid webhook url\"}"),
            NullLogger<WeComNotifier>.Instance);

        var result = await notifier.SendAsync(CreateNotification(), "{\"webhookUrl\":\"https://example.com/hook\"}");

        Assert.False(result.Succeeded);
        Assert.Contains("93000", result.ErrorMessage);
        Assert.Contains("invalid webhook url", result.ErrorMessage);
    }

    [Fact]
    public async Task WeCom_ReturnsSuccess_WhenErrcodeIsZero()
    {
        var notifier = new WeComNotifier(
            new StubHttpClientFactory(HttpStatusCode.OK, "{\"errcode\":0,\"errmsg\":\"ok\"}"),
            NullLogger<WeComNotifier>.Instance);

        var result = await notifier.SendAsync(CreateNotification(), "{\"webhookUrl\":\"https://example.com/hook\"}");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task DingTalk_ReturnsFailure_WhenHttpStatusIsNotSuccess()
    {
        var notifier = new DingTalkNotifier(
            new StubHttpClientFactory(HttpStatusCode.InternalServerError, "boom"),
            NullLogger<DingTalkNotifier>.Instance);

        var result = await notifier.SendAsync(CreateNotification(), "{\"webhookUrl\":\"https://example.com/hook\"}");

        Assert.False(result.Succeeded);
        Assert.Contains("500", result.ErrorMessage);
    }

    private static AlertNotification CreateNotification() => new()
    {
        Title = "测试告警",
        Message = "错误数超阈值",
        Severity = AlertSeverity.P2,
        ProjectName = "demo",
        RuleName = "错误数超阈值",
        TriggeredAt = DateTimeOffset.UtcNow,
        Status = AlertEventStatus.Firing
    };

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpStatusCode status, string body)
            => _client = new HttpClient(new StubHandler(status, body));

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
    }
}
