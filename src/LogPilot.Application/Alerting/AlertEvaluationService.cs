using System.Collections.Concurrent;
using System.Text.Json;
using LogPilot.Application.Common;
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

    /// <summary>告警通知中附带的样本日志条数上限。</summary>
    private const int SampleLogLimit = 3;

    /// <summary>
    /// 通知发送失败后的重试节流：key = 规则ID|事件指纹，value = 上次尝试发送的时间。
    /// 仅在发送失败时写入，成功后移除；成功通知的时间以数据库 last_notified_at 为准。
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _notifyAttempts = new();

    /// <summary>
    /// ForMinutes 持续命中判定：key = 规则ID，value = 条件首次命中的时间。
    /// 条件不再满足时移除，进程重启后重新计时（内存态，不做持久化）。
    /// </summary>
    private readonly ConcurrentDictionary<long, DateTimeOffset> _conditionSince = new();

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

    internal async Task EvaluateAllRulesAsync(CancellationToken cancellationToken)
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

        var storeFailures = 0;
        foreach (var rule in rules)
        {
            try
            {
                await EvaluateRuleAsync(rule, eventRepo, channelRepo, searcher, dispatcher, projectRepo, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AppException ex) when (ex.Code == LogStoreErrors.UnavailableCode)
            {
                // 存储不可用时本轮无法判断规则状态：保持已有告警事件不变，绝不当成“已恢复”。
                storeFailures++;
                _logger.LogWarning(ex, "Skipped alert rule {RuleId}: log store unavailable.", rule.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate rule {RuleId}: {RuleName}", rule.Id, rule.Name);
            }
        }

        if (storeFailures > 0)
        {
            // 让本轮评估计为失败，避免健康检查误判告警引擎正常。
            throw new InvalidOperationException(
                $"日志存储不可用，{storeFailures} 条告警规则本轮未评估。");
        }
    }

    /// <summary>
    /// 评估单条规则。存储不可用时抛出 <see cref="LogStoreErrors"/>，由调用方跳过本轮。
    /// </summary>
    internal async Task EvaluateRuleAsync(
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
            rule.Environment,
            levels,
            rule.Keyword,
            rule.ExceptionType,
            cancellationToken).ConfigureAwait(false);

        long value;
        bool conditionMet;
        if (rule.ConditionType == AlertConditionType.Ratio)
        {
            // 分母：同一服务/环境范围内全部日志（忽略级别/关键词/异常类型过滤）
            var total = await searcher.CountAsync(
                rule.ProjectId,
                start,
                end,
                rule.ServiceName,
                rule.Environment,
                Array.Empty<string>(),
                null,
                null,
                cancellationToken).ConfigureAwait(false);

            var percent = total > 0 ? matched * 100d / total : 0d;
            value = (long)Math.Round(percent);
            conditionMet = total > 0 && percent >= rule.Threshold;
        }
        else
        {
            value = matched;
            conditionMet = matched >= rule.Threshold;
        }

        // ForMinutes > 0 时要求条件“持续命中”达该时长才触发：记录首次命中时间，未满则暂不触发。
        var isFiring = ApplyForMinutesGate(rule, conditionMet, now);

        var silenced = rule.SilenceUntil.HasValue && now < rule.SilenceUntil.Value;
        var fingerprint = BuildFingerprint(rule);

        var existingEvent = await eventRepo.FindActiveByFingerprintAsync(rule.Id, fingerprint, cancellationToken)
            .ConfigureAwait(false);

        if (isFiring)
        {
            await HandleFiringAsync(rule, existingEvent, value, fingerprint, silenced, now, start, end, eventRepo, channelRepo, searcher, dispatcher, projectRepo, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (conditionMet)
        {
            // 条件仍然满足，只是持续命中时长达标前暂缓触发：保持现有事件状态，绝不能当成“已恢复”。
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
        DateTimeOffset start,
        DateTimeOffset end,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        ILogSearcher searcher,
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
            // 新告警：先落库为“未通知”，发送成功后再记录通知时间与次数
            var sampleLogs = await FetchSampleLogsAsync(rule, searcher, start, end, cancellationToken).ConfigureAwait(false);
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
                LastNotifiedAt = null,
                NotifyCount = 0
            };

            var eventId = await eventRepo.InsertOrUpdateAsync(alertEvent, cancellationToken).ConfigureAwait(false);

            var notified = await NotifyEventAsync(
                rule, alertEvent, eventId, projectName, eventRepo, channelRepo, dispatcher, now, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Alert fired: rule={RuleId} event={EventId} value={Value} notified={Notified}",
                rule.Id, eventId, value, notified);
        }
        else if (ShouldAttemptNotification(rule, existingEvent, now))
        {
            // 静默期结束时把事件从 Silenced 恢复为 Firing
            var status = existingEvent.Status;
            if (status == AlertEventStatus.Silenced)
            {
                status = AlertEventStatus.Firing;
                await eventRepo.UpdateStatusAsync(existingEvent.Id, status, null, cancellationToken)
                    .ConfigureAwait(false);
            }

            var notificationEvent = new AlertEvent
            {
                Id = existingEvent.Id,
                RuleId = existingEvent.RuleId,
                ProjectId = existingEvent.ProjectId,
                Fingerprint = existingEvent.Fingerprint,
                Status = status,
                Severity = existingEvent.Severity,
                Title = existingEvent.Title,
                Message = existingEvent.Message,
                SampleLogsJson = existingEvent.SampleLogsJson,
                TriggeredAt = existingEvent.TriggeredAt,
                AcknowledgedAt = existingEvent.AcknowledgedAt,
                ResolvedAt = existingEvent.ResolvedAt,
                LastNotifiedAt = existingEvent.LastNotifiedAt,
                NotifyCount = existingEvent.NotifyCount
            };

            var notified = await NotifyEventAsync(
                rule, notificationEvent, existingEvent.Id, projectName, eventRepo, channelRepo, dispatcher, now, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Alert re-notified: rule={RuleId} event={EventId} value={Value} notified={Notified}",
                rule.Id, existingEvent.Id, value, notified);
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

        // 上次恢复通知发送失败后的冷却节流：冷却期内保持未恢复状态，等待下一轮重试。
        var attemptKey = BuildAttemptKey(rule.Id, existingEvent.Id);
        if (_notifyAttempts.TryGetValue(attemptKey, out var lastAttempt)
            && now - lastAttempt < TimeSpan.FromMinutes(rule.CooldownMinutes))
        {
            return;
        }

        var project = await projectRepo.FindByIdAsync(rule.ProjectId, cancellationToken).ConfigureAwait(false);
        var projectName = project?.Name ?? $"项目#{rule.ProjectId}";

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

        var results = await SendToChannelsAsync(rule, notification, channelRepo, dispatcher, cancellationToken)
            .ConfigureAwait(false);

        if (results.Any(r => !r.Succeeded))
        {
            // 发送失败：事件保持未恢复，冷却结束后自动重试，避免恢复通知永久丢失。
            _notifyAttempts[attemptKey] = now;
            _logger.LogWarning(
                "Alert recovery notification failed: rule={RuleId} event={EventId} errors={Errors}",
                rule.Id, existingEvent.Id,
                string.Join("; ", results.Where(r => !r.Succeeded).Select(r => r.ErrorMessage)));
            return;
        }

        _notifyAttempts.TryRemove(attemptKey, out _);

        // 没有任何可用渠道时无需发送，直接置为已恢复；否则与触发通知一致：发送成功后再落库。
        var delivered = results.Count > 0;

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
            LastNotifiedAt = delivered ? now : existingEvent.LastNotifiedAt,
            NotifyCount = delivered ? existingEvent.NotifyCount + 1 : existingEvent.NotifyCount
        };

        await eventRepo.InsertOrUpdateAsync(resolvedEvent, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Alert recovered: rule={RuleId} event={EventId} notified={Notified}", rule.Id, existingEvent.Id, delivered);
    }

    /// <summary>
    /// 组装告警通知并发送，按发送结果更新事件的通知时间与次数。
    /// </summary>
    private async Task<bool> NotifyEventAsync(
        AlertRule rule,
        AlertEvent alertEvent,
        long eventId,
        string projectName,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
            SampleLogs = ParseSampleLogs(alertEvent.SampleLogsJson)
        };

        return await TryNotifyAsync(
            rule, notification, eventId, alertEvent.NotifyCount, eventRepo, channelRepo, dispatcher, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 发送通知：只有全部渠道都发送成功，才记录 last_notified_at 并累加通知次数；
    /// 失败（含无可用渠道）只记录上次尝试时间用于冷却节流，事件保持“未通知”，下轮继续重试。
    /// </summary>
    private async Task<bool> TryNotifyAsync(
        AlertRule rule,
        AlertNotification notification,
        long eventId,
        int notifyCount,
        IAlertEventRepository eventRepo,
        IAlertChannelRepository channelRepo,
        INotificationDispatcher dispatcher,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var results = await SendToChannelsAsync(rule, notification, channelRepo, dispatcher, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count == 0)
        {
            _notifyAttempts[BuildAttemptKey(rule.Id, eventId)] = now;
            _logger.LogWarning(
                "Alert notification not sent: rule={RuleId} event={EventId} reason=无可用通知渠道",
                rule.Id, eventId);
            return false;
        }

        var failures = results.Where(r => !r.Succeeded).ToList();
        if (failures.Count > 0)
        {
            _notifyAttempts[BuildAttemptKey(rule.Id, eventId)] = now;
            _logger.LogWarning(
                "Alert notification failed: rule={RuleId} event={EventId} failed={Failed}/{Total} errors={Errors}",
                rule.Id, eventId, failures.Count, results.Count,
                string.Join("; ", failures.Select(f => f.ErrorMessage)));
            return false;
        }

        _notifyAttempts.TryRemove(BuildAttemptKey(rule.Id, eventId), out _);
        await eventRepo.UpdateNotifyInfoAsync(eventId, now, notifyCount + 1, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// 判断已有活跃事件是否需要再次发送通知：从未成功通知过则重试（受失败冷却约束），
    /// 或距上次成功通知已达到 RepeatMinutes 配置的重发间隔。
    /// </summary>
    private bool ShouldAttemptNotification(AlertRule rule, AlertEvent existingEvent, DateTimeOffset now)
    {
        if (_notifyAttempts.TryGetValue(BuildAttemptKey(rule.Id, existingEvent.Id), out var lastAttempt)
            && now - lastAttempt < TimeSpan.FromMinutes(rule.CooldownMinutes))
        {
            // 上次发送失败，冷却期内不重试
            return false;
        }

        if (existingEvent.LastNotifiedAt is null)
        {
            return true;
        }

        return rule.RepeatMinutes.HasValue
            && now - existingEvent.LastNotifiedAt.Value >= TimeSpan.FromMinutes(rule.RepeatMinutes.Value);
    }

    private static string BuildAttemptKey(long ruleId, long eventId) => $"{ruleId}|{eventId}";

    /// <summary>
    /// 应用 ForMinutes 持续命中约束：ForMinutes &lt;= 0 时条件命中即触发；
    /// 否则记录首次命中时间，需持续命中达到该时长才返回 true。条件不满足时清除计时。
    /// </summary>
    private bool ApplyForMinutesGate(AlertRule rule, bool conditionMet, DateTimeOffset now)
    {
        if (!conditionMet)
        {
            _conditionSince.TryRemove(rule.Id, out _);
            return false;
        }

        if (rule.ForMinutes <= 0)
        {
            return true;
        }

        var since = _conditionSince.GetOrAdd(rule.Id, now);
        return now - since >= TimeSpan.FromMinutes(rule.ForMinutes);
    }

    private async Task<IReadOnlyList<NotifyResult>> SendToChannelsAsync(
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
            return Array.Empty<NotifyResult>();
        }

        return await dispatcher.SendToChannelsAsync(channels, notification, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 拉取触发告警的样本日志（按规则条件在窗口内查询，最多 <see cref="SampleLogLimit"/> 条）。
    /// </summary>
    private static async Task<IReadOnlyList<SampleLog>> FetchSampleLogsAsync(
        AlertRule rule,
        ILogSearcher searcher,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        var query = new AuthorizedLogQuery
        {
            ProjectId = rule.ProjectId,
            StartTime = start,
            EndTime = end,
            Services = string.IsNullOrWhiteSpace(rule.ServiceName)
                ? Array.Empty<string>()
                : new[] { rule.ServiceName },
            Levels = ParseLevels(rule.Levels),
            Keyword = rule.Keyword,
            PageSize = SampleLogLimit,
            Offset = 0
        };

        var result = await searcher.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        if (result.Items.Count == 0)
        {
            return Array.Empty<SampleLog>();
        }

        return result.Items.Select(item => new SampleLog
        {
            Timestamp = item.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            Level = item.Level,
            ServiceName = item.ServiceName,
            Message = item.Message,
            TraceId = item.TraceId
        }).ToList();
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
