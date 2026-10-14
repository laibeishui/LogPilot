using System.Net.Http.Json;
using System.Text;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.Notifications;

/// <summary>
/// 通用 Webhook 通知（POST JSON）。
/// configJson: {"url":"https://xxx","headers":{"Authorization":"Bearer xxx"},"method":"POST"}
/// </summary>
public sealed class WebhookNotifier : INotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookNotifier> _logger;

    public WebhookNotifier(IHttpClientFactory httpClientFactory, ILogger<WebhookNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public AlertChannelType ChannelType => AlertChannelType.Webhook;

    public async Task<NotifyResult> SendAsync(AlertNotification notification, string configJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseConfig(configJson);
            if (config is null || string.IsNullOrWhiteSpace(config.Url))
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = "Webhook url 未配置" };
            }

            var payload = new
            {
                title = notification.Title,
                message = notification.Message,
                severity = notification.Severity.ToString(),
                status = notification.Status.ToString(),
                projectName = notification.ProjectName,
                serviceName = notification.ServiceName,
                ruleName = notification.RuleName,
                triggeredAt = notification.TriggeredAt.ToString("o"),
                sampleLogs = notification.SampleLogs.Select(x => new
                {
                    timestamp = x.Timestamp,
                    level = x.Level,
                    serviceName = x.ServiceName,
                    message = x.Message,
                    traceId = x.TraceId
                }).ToList()
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var request = new HttpRequestMessage(HttpMethod.Post, config.Url)
            {
                Content = JsonContent.Create(payload)
            };

            foreach (var header in config.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = $"Webhook 返回 HTTP {(int)response.StatusCode}: {responseText}" };
            }

            return new NotifyResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Webhook 通知发送失败");
            return new NotifyResult { Succeeded = false, ErrorMessage = ex.Message };
        }
    }

    private static WebhookConfig? ParseConfig(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var headers = new Dictionary<string, string>();
            if (root.TryGetProperty("headers", out var headersObj))
            {
                foreach (var prop in headersObj.EnumerateObject())
                {
                    headers[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }

            return new WebhookConfig
            {
                Url = root.TryGetProperty("url", out var u) ? u.GetString() : null,
                Headers = headers
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed class WebhookConfig
    {
        public string? Url { get; init; }
        public Dictionary<string, string> Headers { get; init; } = new();
    }
}
