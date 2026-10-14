using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.Notifications;

/// <summary>
/// 钉钉群机器人通知。支持加签（可选）。
/// configJson: {"webhookUrl":"...","secret":"可选"}
/// </summary>
public sealed class DingTalkNotifier : INotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DingTalkNotifier> _logger;

    public DingTalkNotifier(IHttpClientFactory httpClientFactory, ILogger<DingTalkNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public AlertChannelType ChannelType => AlertChannelType.DingTalk;

    public async Task<NotifyResult> SendAsync(AlertNotification notification, string configJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseConfig(configJson);
            if (config is null || string.IsNullOrWhiteSpace(config.WebhookUrl))
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = "钉钉 webhookUrl 未配置" };
            }

            var url = config.WebhookUrl;
            if (!string.IsNullOrWhiteSpace(config.Secret))
            {
                url = AppendSign(url, config.Secret);
            }

            var text = BuildMarkdown(notification);
            var body = new
            {
                msgtype = "markdown",
                markdown = new { title = notification.Title, text = text }
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = $"钉钉返回 HTTP {(int)response.StatusCode}: {responseText}" };
            }

            // 钉钉返回 {"errcode":0,"errmsg":"ok"}
            return new NotifyResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "钉钉通知发送失败");
            return new NotifyResult { Succeeded = false, ErrorMessage = ex.Message };
        }
    }

    private static string BuildMarkdown(AlertNotification n)
    {
        var statusIcon = n.Status == AlertEventStatus.Resolved ? "✅" : "🔥";
        var sb = new StringBuilder();
        sb.AppendLine($"### {statusIcon} {n.Title}");
        sb.AppendLine();
        sb.AppendLine($"- **项目**: {n.ProjectName}");
        if (!string.IsNullOrWhiteSpace(n.ServiceName))
            sb.AppendLine($"- **服务**: {n.ServiceName}");
        sb.AppendLine($"- **级别**: {n.Severity}");
        sb.AppendLine($"- **规则**: {n.RuleName}");
        sb.AppendLine($"- **时间**: {n.TriggeredAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("**详情**:");
        sb.AppendLine("> " + n.Message.Replace("\n", "\n> "));

        if (n.SampleLogs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**样本日志**:");
            foreach (var log in n.SampleLogs.Take(3))
            {
                sb.AppendLine($"- `{log.Timestamp}` [{log.Level}] {log.ServiceName}: {Truncate(log.Message, 100)}");
            }
        }

        return sb.ToString();
    }

    private static string AppendSign(string url, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var sign = Uri.EscapeDataString(Convert.ToBase64String(hash));
        var sep = url.Contains('?') ? "&" : "?";
        return $"{url}{sep}timestamp={timestamp}&sign={sign}";
    }

    private static DingTalkConfig? ParseConfig(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new DingTalkConfig
            {
                WebhookUrl = root.TryGetProperty("webhookUrl", out var u) ? u.GetString() : null,
                Secret = root.TryGetProperty("secret", out var s) ? s.GetString() : null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class DingTalkConfig
    {
        public string? WebhookUrl { get; init; }
        public string? Secret { get; init; }
    }
}
