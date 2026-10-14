using System.Net.Http.Json;
using System.Text;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.Notifications;

/// <summary>
/// 企业微信群机器人通知。
/// configJson: {"webhookUrl":"https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=xxx"}
/// </summary>
public sealed class WeComNotifier : INotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeComNotifier> _logger;

    public WeComNotifier(IHttpClientFactory httpClientFactory, ILogger<WeComNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public AlertChannelType ChannelType => AlertChannelType.WeCom;

    public async Task<NotifyResult> SendAsync(AlertNotification notification, string configJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseConfig(configJson);
            if (config is null || string.IsNullOrWhiteSpace(config.WebhookUrl))
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = "企微 webhookUrl 未配置" };
            }

            var markdown = BuildMarkdown(notification);
            var body = new
            {
                msgtype = "markdown",
                markdown = new { content = markdown }
            };

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);
            var response = await client.PostAsJsonAsync(config.WebhookUrl, body, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = $"企微返回 HTTP {(int)response.StatusCode}: {responseText}" };
            }

            return new NotifyResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "企微通知发送失败");
            return new NotifyResult { Succeeded = false, ErrorMessage = ex.Message };
        }
    }

    private static string BuildMarkdown(AlertNotification n)
    {
        var statusIcon = n.Status == AlertEventStatus.Resolved ? "<font color=\"info\">✅ 已恢复</font>" : "<font color=\"warning\">🔥 告警</font>";
        var sb = new StringBuilder();
        sb.AppendLine($"### {statusIcon} {n.Title}");
        sb.AppendLine($"> **项目**: {n.ProjectName}");
        if (!string.IsNullOrWhiteSpace(n.ServiceName))
            sb.AppendLine($"> **服务**: {n.ServiceName}");
        sb.AppendLine($"> **级别**: {n.Severity}");
        sb.AppendLine($"> **规则**: {n.RuleName}");
        sb.AppendLine($"> **时间**: {n.TriggeredAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine(n.Message);

        if (n.SampleLogs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**样本日志**:");
            foreach (var log in n.SampleLogs.Take(3))
            {
                sb.AppendLine($"> `{log.Timestamp}` [{log.Level}] {log.ServiceName}: {Truncate(log.Message, 100)}");
            }
        }

        return sb.ToString();
    }

    private static WeComConfig? ParseConfig(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return new WeComConfig
            {
                WebhookUrl = doc.RootElement.TryGetProperty("webhookUrl", out var u) ? u.GetString() : null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private sealed class WeComConfig
    {
        public string? WebhookUrl { get; init; }
    }
}
