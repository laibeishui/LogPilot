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

            // 企微即便 HTTP 200 也可能在响应体中返回业务错误，例如
            // {"errcode":93000,"errmsg":"invalid webhook url"}，必须解析 errcode 判定成败。
            if (TryParseErrcode(responseText, out var errcode, out var errmsg) && errcode != 0)
            {
                return new NotifyResult
                {
                    Succeeded = false,
                    ErrorMessage = $"企微返回业务错误 errcode={errcode}: {errmsg}"
                };
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

    /// <summary>
    /// 解析企微响应体中的 errcode/errmsg。返回 false 表示响应不是预期的 JSON 结构（如网关返回的非 JSON 内容）。
    /// </summary>
    private static bool TryParseErrcode(string responseText, out int errcode, out string errmsg)
    {
        errcode = 0;
        errmsg = string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object
                || !root.TryGetProperty("errcode", out var codeElement)
                || codeElement.ValueKind != System.Text.Json.JsonValueKind.Number)
            {
                return false;
            }

            errcode = codeElement.GetInt32();
            if (root.TryGetProperty("errmsg", out var msgElement)
                && msgElement.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                errmsg = msgElement.GetString() ?? string.Empty;
            }

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private sealed class WeComConfig
    {
        public string? WebhookUrl { get; init; }
    }
}
