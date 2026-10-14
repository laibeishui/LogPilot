using System.Net;
using System.Net.Mail;
using System.Text;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.Notifications;

/// <summary>
/// 邮件通知（SMTP）。
/// configJson: {"smtpHost":"smtp.example.com","smtpPort":587,"username":"xxx","password":"xxx","from":"xxx@xx.com","to":["a@b.com"],"enableSsl":true}
/// </summary>
public sealed class EmailNotifier : INotifier
{
    private readonly ILogger<EmailNotifier> _logger;

    public EmailNotifier(ILogger<EmailNotifier> logger)
    {
        _logger = logger;
    }

    public AlertChannelType ChannelType => AlertChannelType.Email;

    public async Task<NotifyResult> SendAsync(AlertNotification notification, string configJson, CancellationToken cancellationToken = default)
    {
        try
        {
            var config = ParseConfig(configJson);
            if (config is null || string.IsNullOrWhiteSpace(config.SmtpHost) || config.To.Count == 0)
            {
                return new NotifyResult { Succeeded = false, ErrorMessage = "邮件配置不完整" };
            }

            using var client = new SmtpClient(config.SmtpHost, config.SmtpPort)
            {
                EnableSsl = config.EnableSsl,
                Credentials = string.IsNullOrWhiteSpace(config.Username)
                    ? null
                    : new NetworkCredential(config.Username, config.Password),
                Timeout = 10000
            };

            var subject = notification.Status == AlertEventStatus.Resolved
                ? $"[恢复] {notification.Title}"
                : $"[告警][{notification.Severity}] {notification.Title}";

            var body = BuildBody(notification);

            var from = string.IsNullOrWhiteSpace(config.From) ? config.Username ?? "logpilot@localhost" : config.From;
            using var message = new MailMessage
            {
                From = new MailAddress(from),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            foreach (var to in config.To)
            {
                message.To.Add(to);
            }

            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            return new NotifyResult { Succeeded = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "邮件通知发送失败");
            return new NotifyResult { Succeeded = false, ErrorMessage = ex.Message };
        }
    }

    private static string BuildBody(AlertNotification n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"项目: {n.ProjectName}");
        if (!string.IsNullOrWhiteSpace(n.ServiceName))
            sb.AppendLine($"服务: {n.ServiceName}");
        sb.AppendLine($"级别: {n.Severity}");
        sb.AppendLine($"规则: {n.RuleName}");
        sb.AppendLine($"时间: {n.TriggeredAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"状态: {n.Status}");
        sb.AppendLine();
        sb.AppendLine("详情:");
        sb.AppendLine(n.Message);

        if (n.SampleLogs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("样本日志:");
            foreach (var log in n.SampleLogs.Take(5))
            {
                sb.AppendLine($"  [{log.Timestamp}] [{log.Level}] {log.ServiceName}: {log.Message}");
            }
        }

        return sb.ToString();
    }

    private static EmailConfig? ParseConfig(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var to = new List<string>();
            if (root.TryGetProperty("to", out var toArr))
            {
                foreach (var item in toArr.EnumerateArray())
                {
                    var val = item.GetString();
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        to.Add(val);
                    }
                }
            }

            return new EmailConfig
            {
                SmtpHost = root.TryGetProperty("smtpHost", out var h) ? h.GetString() : null,
                SmtpPort = root.TryGetProperty("smtpPort", out var p) ? p.GetInt32() : 587,
                Username = root.TryGetProperty("username", out var u) ? u.GetString() : null,
                Password = root.TryGetProperty("password", out var pw) ? pw.GetString() : null,
                From = root.TryGetProperty("from", out var f) ? f.GetString() : null,
                EnableSsl = root.TryGetProperty("enableSsl", out var e) && e.GetBoolean(),
                To = to
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed class EmailConfig
    {
        public string? SmtpHost { get; init; }
        public int SmtpPort { get; init; } = 587;
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? From { get; init; }
        public bool EnableSsl { get; init; } = true;
        public List<string> To { get; init; } = new();
    }
}
