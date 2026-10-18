using System.Text.Json;
using LogPilot.Application.Common;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Alerting;

public interface IAlertRuleService
{
    Task<IReadOnlyList<AlertRuleDto>> ListAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task<AlertRuleDto> GetAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, CancellationToken cancellationToken = default);

    Task<AlertRuleDto> CreateAsync(long userId, bool isSystemAdmin, long projectId, CreateAlertRuleRequest request, CancellationToken cancellationToken = default);

    Task<AlertRuleDto> UpdateAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, UpdateAlertRuleRequest request, CancellationToken cancellationToken = default);

    Task<AlertRuleDto> SetSilenceAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, int? minutes, CancellationToken cancellationToken = default);

    Task DeleteAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, CancellationToken cancellationToken = default);
}

public sealed class AlertRuleService : IAlertRuleService
{
    private readonly IAlertRuleRepository _rules;
    private readonly IAlertChannelRepository _channels;
    private readonly IProjectAccessService _access;

    public AlertRuleService(
        IAlertRuleRepository rules,
        IAlertChannelRepository channels,
        IProjectAccessService access)
    {
        _rules = rules;
        _channels = channels;
        _access = access;
    }

    public async Task<IReadOnlyList<AlertRuleDto>> ListAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var items = await _rules.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return items.Select(Map).ToList();
    }

    public async Task<AlertRuleDto> GetAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var rule = await GetRuleOrThrow(ruleId, projectId, cancellationToken);
        return Map(rule);
    }

    public async Task<AlertRuleDto> CreateAsync(long userId, bool isSystemAdmin, long projectId, CreateAlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "规则名称必填且不超过 100 字符。");
        }

        if (request.ConditionType == AlertConditionType.Ratio)
        {
            if (request.Threshold is <= 0 or > 100)
            {
                throw new AppException("INVALID_ARGUMENT", "占比阈值必须在 1~100 之间。");
            }
        }
        else if (request.Threshold <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "阈值必须大于 0。");
        }

        if (request.WindowMinutes is <= 0 or > 1440)
        {
            throw new AppException("INVALID_ARGUMENT", "时间窗口必须在 1~1440 分钟之间。");
        }

        ValidateDurations(request.ForMinutes ?? 0, request.CooldownMinutes ?? 5, request.RepeatMinutes);

        if (request.ChannelIds is null || request.ChannelIds.Count == 0)
        {
            throw new AppException("INVALID_ARGUMENT", "至少选择一个通知渠道。");
        }

        // 验证渠道都属于本项目
        var projectChannels = await _channels.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        var projectChannelIds = projectChannels.Select(c => c.Id).ToHashSet();
        if (request.ChannelIds.Any(id => !projectChannelIds.Contains(id)))
        {
            throw new AppException("INVALID_ARGUMENT", "包含不存在或无权访问的通知渠道。");
        }

        var now = DateTimeOffset.UtcNow;
        var rule = new AlertRule
        {
            ProjectId = projectId,
            Name = name,
            Description = (request.Description ?? string.Empty).Trim(),
            Enabled = request.Enabled is false ? EntityStatus.Disabled : EntityStatus.Enabled,
            ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim(),
            Environment = string.IsNullOrWhiteSpace(request.Environment) ? null : request.Environment.Trim(),
            Levels = request.Levels is { Count: > 0 } ? JsonSerializer.Serialize(request.Levels) : null,
            Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim(),
            ExceptionType = string.IsNullOrWhiteSpace(request.ExceptionType) ? null : request.ExceptionType.Trim(),
            ConditionType = request.ConditionType,
            Threshold = request.Threshold,
            WindowMinutes = request.WindowMinutes,
            ForMinutes = request.ForMinutes ?? 0,
            ChannelIds = JsonSerializer.Serialize(request.ChannelIds),
            Severity = request.Severity ?? AlertSeverity.P2,
            CooldownMinutes = request.CooldownMinutes ?? 5,
            NotifyRecovery = request.NotifyRecovery ?? true,
            RepeatMinutes = request.RepeatMinutes,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _rules.InsertAsync(rule, cancellationToken).ConfigureAwait(false);
        var created = await _rules.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return Map(created!);
    }

    public async Task<AlertRuleDto> UpdateAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, UpdateAlertRuleRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var existing = await GetRuleOrThrow(ruleId, projectId, cancellationToken);

        var name = request.Name is null ? existing.Name : request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "规则名称必填且不超过 100 字符。");
        }

        var conditionType = request.ConditionType ?? existing.ConditionType;
        var threshold = request.Threshold ?? existing.Threshold;
        if (conditionType == AlertConditionType.Ratio)
        {
            if (threshold is <= 0 or > 100)
            {
                throw new AppException("INVALID_ARGUMENT", "占比阈值必须在 1~100 之间。");
            }
        }
        else if (threshold <= 0)
        {
            throw new AppException("INVALID_ARGUMENT", "阈值必须大于 0。");
        }

        var windowMinutes = request.WindowMinutes ?? existing.WindowMinutes;
        if (windowMinutes is <= 0 or > 1440)
        {
            throw new AppException("INVALID_ARGUMENT", "时间窗口必须在 1~1440 分钟之间。");
        }

        var forMinutes = request.ForMinutes ?? existing.ForMinutes;
        var cooldownMinutes = request.CooldownMinutes ?? existing.CooldownMinutes;
        // RepeatMinutes 未传（null）时保留原值，避免 PATCH 局部更新把已配置的重发间隔清空。
        var repeatMinutes = request.RepeatMinutes ?? existing.RepeatMinutes;
        ValidateDurations(forMinutes, cooldownMinutes, repeatMinutes);

        IReadOnlyList<long> channelIds;
        if (request.ChannelIds is not null)
        {
            if (request.ChannelIds.Count == 0)
            {
                throw new AppException("INVALID_ARGUMENT", "至少选择一个通知渠道。");
            }

            var projectChannels = await _channels.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
            var projectChannelIds = projectChannels.Select(c => c.Id).ToHashSet();
            if (request.ChannelIds.Any(id => !projectChannelIds.Contains(id)))
            {
                throw new AppException("INVALID_ARGUMENT", "包含不存在或无权访问的通知渠道。");
            }

            channelIds = request.ChannelIds;
        }
        else
        {
            channelIds = JsonSerializer.Deserialize<List<long>>(existing.ChannelIds) ?? new List<long>();
        }

        var updated = new AlertRule
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            Name = name,
            Description = request.Description is null ? existing.Description : request.Description.Trim(),
            Enabled = request.Enabled.HasValue
                ? (request.Enabled.Value ? EntityStatus.Enabled : EntityStatus.Disabled)
                : existing.Enabled,
            ServiceName = request.ServiceName is null ? existing.ServiceName : (string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim()),
            Environment = request.Environment is null ? existing.Environment : (string.IsNullOrWhiteSpace(request.Environment) ? null : request.Environment.Trim()),
            Levels = request.Levels is null ? existing.Levels : (request.Levels.Count > 0 ? JsonSerializer.Serialize(request.Levels) : null),
            Keyword = request.Keyword is null ? existing.Keyword : (string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim()),
            ExceptionType = request.ExceptionType is null ? existing.ExceptionType : (string.IsNullOrWhiteSpace(request.ExceptionType) ? null : request.ExceptionType.Trim()),
            ConditionType = conditionType,
            Threshold = threshold,
            WindowMinutes = windowMinutes,
            ForMinutes = forMinutes,
            ChannelIds = JsonSerializer.Serialize(channelIds),
            Severity = request.Severity ?? existing.Severity,
            CooldownMinutes = cooldownMinutes,
            NotifyRecovery = request.NotifyRecovery ?? existing.NotifyRecovery,
            RepeatMinutes = repeatMinutes,
            SilenceUntil = existing.SilenceUntil,
            CreatedBy = existing.CreatedBy,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _rules.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        var refreshed = await _rules.FindByIdAsync(ruleId, cancellationToken).ConfigureAwait(false);
        return Map(refreshed!);
    }

    public async Task<AlertRuleDto> SetSilenceAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, int? minutes, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        await GetRuleOrThrow(ruleId, projectId, cancellationToken);

        DateTimeOffset? silenceUntil = minutes is > 0
            ? DateTimeOffset.UtcNow.AddMinutes(minutes.Value)
            : null;

        await _rules.UpdateSilenceAsync(ruleId, silenceUntil, cancellationToken).ConfigureAwait(false);
        var refreshed = await _rules.FindByIdAsync(ruleId, cancellationToken).ConfigureAwait(false);
        return Map(refreshed!);
    }

    public async Task DeleteAsync(long userId, bool isSystemAdmin, long projectId, long ruleId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        await GetRuleOrThrow(ruleId, projectId, cancellationToken);
        await _rules.DeleteAsync(ruleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AlertRule> GetRuleOrThrow(long ruleId, long projectId, CancellationToken cancellationToken)
    {
        var rule = await _rules.FindByIdAsync(ruleId, cancellationToken).ConfigureAwait(false);
        if (rule is null || rule.ProjectId != projectId)
        {
            throw new AppException("NOT_FOUND", "告警规则不存在。", 404);
        }

        return rule;
    }

    /// <summary>
    /// 校验时长类参数：负值或超大值会让告警评估行为异常（例如 0 分钟重发间隔会每轮都发送通知）。
    /// </summary>
    private static void ValidateDurations(int forMinutes, int cooldownMinutes, int? repeatMinutes)
    {
        if (forMinutes is < 0 or > 1440)
        {
            throw new AppException("INVALID_ARGUMENT", "持续命中时长必须在 0~1440 分钟之间。");
        }

        if (cooldownMinutes is < 0 or > 1440)
        {
            throw new AppException("INVALID_ARGUMENT", "冷却时间必须在 0~1440 分钟之间。");
        }

        if (repeatMinutes.HasValue && repeatMinutes.Value is < 1 or > 10080)
        {
            throw new AppException("INVALID_ARGUMENT", "重发间隔必须在 1~10080 分钟之间。");
        }
    }

    private static AlertRuleDto Map(AlertRule r) => new()
    {
        Id = r.Id,
        ProjectId = r.ProjectId,
        Name = r.Name,
        Description = r.Description,
        Enabled = r.Enabled == EntityStatus.Enabled,
        ServiceName = r.ServiceName,
        Environment = r.Environment,
        Levels = string.IsNullOrWhiteSpace(r.Levels) ? Array.Empty<string>() : JsonSerializer.Deserialize<List<string>>(r.Levels) ?? new List<string>(),
        Keyword = r.Keyword,
        ExceptionType = r.ExceptionType,
        ConditionType = r.ConditionType.ToString().ToLowerInvariant(),
        Threshold = r.Threshold,
        WindowMinutes = r.WindowMinutes,
        ForMinutes = r.ForMinutes,
        ChannelIds = JsonSerializer.Deserialize<List<long>>(r.ChannelIds) ?? new List<long>(),
        Severity = r.Severity.ToString(),
        CooldownMinutes = r.CooldownMinutes,
        NotifyRecovery = r.NotifyRecovery,
        RepeatMinutes = r.RepeatMinutes,
        SilenceUntil = r.SilenceUntil,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };
}
