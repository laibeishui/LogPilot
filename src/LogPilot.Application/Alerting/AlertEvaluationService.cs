using System.Text.Json;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogPilot.Application.Alerting;

/// <summary>
/// 告警评估引擎：每 30 秒轮询所有启用规则，匹配 VictoriaLogs 日志，触发/恢复告警并发送通知。
/// </summary>
public sealed class AlertEvaluationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertEvaluationService> _logger;

    // 健康状态
    public DateTimeOffset? LastRunAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public string? LastError { get; private set; }
    public long RunCount { get; private set; }

    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(30);

    public AlertEvaluationService(IServiceScopeFactory scopeFactory, ILogger<AlertEvaluationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alert evaluation service started.");

        // 启动时等待一下，让系统就绪
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                LastRunAt = DateTimeOffset.UtcNow;
                await EvaluateAllRulesAsync(stoppingToken).ConfigureAwait(false);
                LastSuccessAt = DateTimeOffset.UtcNow;
                LastError = null;
                RunCount++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _logger.LogError(ex, "Alert evaluation cycle failed.");
            }

            try
            {
                await Task.Delay(EvaluationInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Alert evaluation service stopped.");
    }

    private async Task EvaluateAllRulesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var ruleRepo = scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>();
        var eventRepo = scope.ServiceProvider.GetRequiredService<IAlertEventRepository>();
        var channelRepo = scope.ServiceProvider.GetRequiredService<IAlertChannelRepository>();
        var searcher = scope.ServiceProvider.GetRequiredService<ILogSearcher>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();

        var rules = await ruleRepo.ListEnabledAsync(cancellationToken).ConfigureAwait(false);
        if (rules.Count == 0)
        {
            return;
        }

        foreach (var rule in rules)
        {
            try
            {
                await EvaluateRuleAsync(rule, eventRepo, channelRepo, searcher, dispatcher, projectRepo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate rule {RuleId}: {RuleName}", rule.Id, rule.Name);
            }
        }
    }

    private async Task EvaluateRuleAsync(
        AlertRule rule,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        ILogSearcher searcher,
        INotificationDispatcher dispatcher,
        IProjectRepository projectRepo,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var end = now;
        var start = end.AddMinutes(-rule.WindowMinutes);

        var levels = ParseLevels(rule.Levels);
        var matched = await searcher.CountAsync(
            rule.ProjectId,
            start,
            end,
            rule.ServiceName,
            levels,
            rule.Keyword,
            rule.ExceptionType,
            cancellationToken).ConfigureAwait(false);

        long value;
        bool isFiring;
        if (rule.ConditionType == AlertConditionType.Ratio)
        {
            // 分母：同一服务范围内全部日志（忽略级别/关键词/异常类型过滤）
            var total = await searcher.CountAsync(
                rule.ProjectId,
                start,
                end,
                rule.ServiceName,
                Array.Empty<string>(),
                null,
                null,
                cancellationToken).ConfigureAwait(false);

            var percent = total > 0 ? matched * 100d / total : 0d;
            value = (long)Math.Round(percent);
            isFiring = total > 0 && percent >= rule.Threshold;
        }
        else
        {
            value = matched;
            isFiring = matched >= rule.Threshold;
        }

        var silenced = rule.SilenceUntil.HasValue && now < rule.SilenceUntil.Value;
        var fingerprint = BuildFingerprint(rule);

        var existingEvent = await eventRepo.FindActiveByFingerprintAsync(rule.Id, fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (isFiring)
        {
            await HandleFiringAsync(rule, existingEvent, value, fingerprint, silenced, now, eventRepo, channelRepo, dispatcher, projectRepo, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (existingEvent is not null && existingEvent.Status != AlertEventStatus.Resolved)
        {
            await HandleRecoveryAsync(rule, existingEvent, silenced, now, eventRepo, channelRepo, dispatcher, projectRepo, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleFiringAsync(
        AlertRule rule,
        AlertEvent? existingEvent,
        long value,
        string fingerprint,
        bool silenced,
        DateTimeOffset now,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        IProjectRepository projectRepo,
        CancellationToken cancellationToken)
    {
        var project = await projectRepo.FindByIdAsync(rule.ProjectId, cancellationToken).ConfigureAwait(false);
        var projectName = project?.Name ?? $"项目#{rule.ProjectId}";

        // 静默期内不发送任何通知，仅记录/标记事件状态。
        if (silenced)
        {
            if (existingEvent is null)
            {
                var muted = new AlertEvent
                {
                    RuleId = rule.Id,
                    ProjectId = rule.ProjectId,
                    Fingerprint = fingerprint,
                    Status = AlertEventStatus.Silenced,
                    Severity = rule.Severity,
                    Title = BuildTitle(rule, value),
                    Message = BuildMessage(rule, value, projectName),
                    SampleLogsJson = JsonSerializer.Serialize(Array.Empty<SampleLog>()),
                    TriggeredAt = now,
                    LastNotifiedAt = null,
                    NotifyCount = 0
                };

                await eventRepo.InsertOrUpdateAsync(muted, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Alert suppressed by silence: rule={RuleId} value={Value}", rule.Id, value);
            }
            else if (existingEvent.Status != AlertEventStatus.Silenced)
            {
                await eventRepo.UpdateStatusAsync(existingEvent.Id, AlertEventStatus.Silenced, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (existingEvent is null)
        {
            // 新告警
            var sampleLogs = await FetchSampleLogsAsync(rule, searcher: null!, cancellationToken); // 样本日志在通知时获取
            var title = BuildTitle(rule, value);
            var message = BuildMessage(rule, value, projectName);

            var alertEvent = new AlertEvent
            {
                RuleId = rule.Id,
                ProjectId = rule.ProjectId,
                Fingerprint = fingerprint,
                Status = AlertEventStatus.Firing,
                Severity = rule.Severity,
                Title = title,
                Message = message,
                SampleLogsJson = JsonSerializer.Serialize(sampleLogs),
                TriggeredAt = now,
                LastNotifiedAt = now,
                NotifyCount = 1
            };

            var eventId = await eventRepo.InsertOrUpdateAsync(alertEvent, cancellationToken).ConfigureAwait(false);

            await SendNotificationAsync(rule, alertEvent, projectName, channelRepo, dispatcher, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Alert fired: rule={RuleId} event={EventId} value={Value}", rule.Id, eventId, value);
        }
        else
        {
            // 已有活跃告警：检查冷却和重发
            var shouldNotify = false;
            var notifyCount = existingEvent.NotifyCount;
            var lastNotifiedAt = existingEvent.LastNotifiedAt;

            if (existingEvent.LastNotifiedAt is null)
            {
                shouldNotify = true;
            }
            else
            {
                var sinceLastNotify = now - existingEvent.LastNotifiedAt.Value;
                if (rule.RepeatMinutes.HasValue && sinceLastNotify >= TimeSpan.FromMinutes(rule.RepeatMinutes.Value))
                {
                    shouldNotify = true;
                }
                else if (sinceLastNotify >= TimeSpan.FromMinutes(rule.CooldownMinutes) && rule.RepeatMinutes is null)
                {
                    // 未配置重发时，仅冷却后通知一次（持续告警）
                    shouldNotify = false;
                }
            }

            if (shouldNotify)
            {
                notifyCount++;
                lastNotifiedAt = now;

                var updatedEvent = new AlertEvent
                {
                    Id = existingEvent.Id,
                    RuleId = existingEvent.RuleId,
                    ProjectId = existingEvent.ProjectId,
                    Fingerprint = existingEvent.Fingerprint,
                    // 静默期结束时把事件从 Silenced 恢复为 Firing
                    Status = existingEvent.Status == AlertEventStatus.Silenced ? AlertEventStatus.Firing : existingEvent.Status,
                    Severity = existingEvent.Severity,
                    Title = existingEvent.Title,
                    Message = existingEvent.Message,
                    SampleLogsJson = existingEvent.SampleLogsJson,
                    TriggeredAt = existingEvent.TriggeredAt,
                    AcknowledgedAt = existingEvent.AcknowledgedAt,
                    ResolvedAt = existingEvent.ResolvedAt,
                    LastNotifiedAt = lastNotifiedAt,
                    NotifyCount = notifyCount
                };

                await eventRepo.InsertOrUpdateAsync(updatedEvent, cancellationToken).ConfigureAwait(false);
                await SendNotificationAsync(rule, updatedEvent, projectName, channelRepo, dispatcher, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task HandleRecoveryAsync(
        AlertRule rule,
        AlertEvent existingEvent,
        bool silenced,
        DateTimeOffset now,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        IProjectRepository projectRepo,
        CancellationToken cancellationToken)
    {
        if (!rule.NotifyRecovery || silenced)
        {
            await eventRepo.UpdateStatusAsync(existingEvent.Id, AlertEventStatus.Resolved, now, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var project = await projectRepo.FindByIdAsync(rule.ProjectId, cancellationToken).ConfigureAwait(false);
        var projectName = project?.Name ?? $"项目#{rule.ProjectId}";

        var resolvedEvent = new AlertEvent
        {
            Id = existingEvent.Id,
            RuleId = existingEvent.RuleId,
            ProjectId = existingEvent.ProjectId,
            Fingerprint = existingEvent.Fingerprint,
            Status = AlertEventStatus.Resolved,
            Severity = existingEvent.Severity,
            Title = existingEvent.Title,
            Message = existingEvent.Message,
            SampleLogsJson = existingEvent.SampleLogsJson,
            TriggeredAt = existingEvent.TriggeredAt,
            AcknowledgedAt = existingEvent.AcknowledgedAt,
            ResolvedAt = now,
            LastNotifiedAt = now,
            NotifyCount = existingEvent.NotifyCount
        };

        await eventRepo.InsertOrUpdateAsync(resolvedEvent, cancellationToken).ConfigureAwait(false);

        var notification = new AlertNotification
        {
            Title = $"[恢复] {existingEvent.Title}",
            Message = $"告警已恢复。\n规则: {rule.Name}\n项目: {projectName}\n持续时长: {FormatDuration(now - existingEvent.TriggeredAt)}",
            Severity = rule.Severity,
            ProjectName = projectName,
            ServiceName = rule.ServiceName,
            RuleName = rule.Name,
            TriggeredAt = existingEvent.TriggeredAt,
            Status = AlertEventStatus.Resolved,
            SampleLogs = Array.Empty<SampleLog>()
        };

        await SendNotificationRawAsync(rule, notification, channelRepo, dispatcher, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("Alert recovered: rule={RuleId} event={EventId}", rule.Id, existingEvent.Id);
    }

    private async Task SendNotificationAsync(
        AlertRule rule,
        AlertEvent alertEvent,
        string projectName,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var sampleLogs = ParseSampleLogs(alertEvent.SampleLogsJson);
        var notification = new AlertNotification
        {
            Title = alertEvent.Title,
            Message = alertEvent.Message,
            Severity = alertEvent.Severity,
            ProjectName = projectName,
            ServiceName = rule.ServiceName,
            RuleName = rule.Name,
            TriggeredAt = alertEvent.TriggeredAt,
            Status = alertEvent.Status,
            SampleLogs = sampleLogs
        };

        await SendNotificationRawAsync(rule, notification, channelRepo, dispatcher, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendNotificationRawAsync(
        AlertRule rule,
        AlertNotification notification,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var channelIds = JsonSerializer.Deserialize<List<long>>(rule.ChannelIds) ?? new List<long>();
        var channels = new List<AlertChannel>();
        foreach (var channelId in channelIds)
        {
            var channel = await channelRepo.FindByIdAsync(channelId, cancellationToken).ConfigureAwait(false);
            if (channel is not null)
            {
                channels.Add(channel);
            }
        }

        if (channels.Count == 0)
        {
            _logger.LogWarning("Rule {RuleId} has no valid channels.", rule.Id);
            return;
        }

        await dispatcher.SendToChannelsAsync(channels, notification, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<SampleLog>> FetchSampleLogsAsync(AlertRule rule, ILogSearcher searcher, CancellationToken cancellationToken)
    {
        // V0.2 简化：样本日志在通知内容中用规则信息描述，不单独查询
        await Task.CompletedTask.ConfigureAwait(false);
        return Array.Empty<SampleLog>();
    }

    private static string BuildFingerprint(AlertRule rule)
    {
        var raw = $"{rule.ProjectId}|{rule.ServiceName ?? "*"}|{rule.Environment ?? "*"}|{rule.Levels ?? "*"}|{rule.Keyword ?? "*"}|{rule.ExceptionType ?? "*"}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string BuildTitle(AlertRule rule, long value)
    {
        var scope = string.IsNullOrWhiteSpace(rule.ServiceName) ? "全部服务" : rule.ServiceName;
        var metric = rule.ConditionType == AlertConditionType.Ratio ? $"错误占比 {value}%" : $"{value} 条";
        return $"[{rule.Severity}] {rule.Name}（{scope}）触发：{metric}";
    }

    private static string BuildMessage(AlertRule rule, long value, string projectName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"规则: {rule.Name}");
        sb.AppendLine($"项目: {projectName}");
        if (!string.IsNullOrWhiteSpace(rule.ServiceName))
            sb.AppendLine($"服务: {rule.ServiceName}");
        if (rule.ConditionType == AlertConditionType.Ratio)
        {
            sb.AppendLine($"条件: 最近 {rule.WindowMinutes} 分钟内错误占比 >= {rule.Threshold}%");
            sb.AppendLine($"当前: {value}%");
        }
        else
        {
            sb.AppendLine($"条件: 最近 {rule.WindowMinutes} 分钟内匹配日志数 >= {rule.Threshold}");
            sb.AppendLine($"当前: {value} 条");
        }

        if (!string.IsNullOrWhiteSpace(rule.Keyword))
            sb.AppendLine($"关键词: {rule.Keyword}");
        return sb.ToString();
    }

    private static IReadOnlyList<string> ParseLevels(string? levelsJson)
    {
        if (string.IsNullOrWhiteSpace(levelsJson))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(levelsJson) ?? new List<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<SampleLog> ParseSampleLogs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<SampleLog>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SampleLog>>(json) ?? new List<SampleLog>();
        }
        catch
        {
            return Array.Empty<SampleLog>();
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1) return $"{(int)duration.TotalSeconds} 秒";
        if (duration.TotalHours < 1) return $"{(int)duration.TotalMinutes} 分钟";
        return $"{(int)duration.TotalHours} 小时 {(int)duration.Minutes} 分钟";
    }
}
