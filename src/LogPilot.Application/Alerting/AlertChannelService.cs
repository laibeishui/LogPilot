using System.Text.Json;
using LogPilot.Application.Common;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Alerting;

public sealed class AlertChannelService : IAlertChannelService
{
    private readonly IAlertChannelRepository _channels;
    private readonly IProjectAccessService _access;
    private readonly INotificationDispatcher _dispatcher;

    public AlertChannelService(
        IAlertChannelRepository channels,
        IProjectAccessService access,
        INotificationDispatcher dispatcher)
    {
        _channels = channels;
        _access = access;
        _dispatcher = dispatcher;
    }

    public async Task<IReadOnlyList<AlertChannelDto>> ListAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var items = await _channels.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return items.Select(Map).ToList();
    }

    public async Task<AlertChannelDto> CreateAsync(long userId, bool isSystemAdmin, long projectId, CreateAlertChannelRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);

        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "渠道名称必填且不超过 100 字符。");
        }

        ValidateConfig(request.Type, request.ConfigJson);

        var now = DateTimeOffset.UtcNow;
        var channel = new AlertChannel
        {
            ProjectId = projectId,
            Name = name,
            Type = request.Type,
            ConfigJson = request.ConfigJson,
            Enabled = request.Enabled is false ? EntityStatus.Disabled : EntityStatus.Enabled,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _channels.InsertAsync(channel, cancellationToken).ConfigureAwait(false);
        var created = await _channels.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return Map(created!);
    }

    public async Task<AlertChannelDto> UpdateAsync(long userId, bool isSystemAdmin, long projectId, long channelId, UpdateAlertChannelRequest request, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);

        var existing = await GetChannelOrThrow(channelId, projectId, cancellationToken);

        var name = request.Name is null ? existing.Name : request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "渠道名称必填且不超过 100 字符。");
        }

        var type = request.Type ?? existing.Type;
        var configJson = existing.ConfigJson;
        if (!string.IsNullOrWhiteSpace(request.ConfigJson))
        {
            ValidateConfig(type, request.ConfigJson);
            configJson = request.ConfigJson;
        }

        var updated = new AlertChannel
        {
            Id = existing.Id,
            ProjectId = existing.ProjectId,
            Name = name,
            Type = type,
            ConfigJson = configJson,
            Enabled = request.Enabled.HasValue
                ? (request.Enabled.Value ? EntityStatus.Enabled : EntityStatus.Disabled)
                : existing.Enabled,
            CreatedBy = existing.CreatedBy,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _channels.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        var refreshed = await _channels.FindByIdAsync(channelId, cancellationToken).ConfigureAwait(false);
        return Map(refreshed!);
    }

    public async Task DeleteAsync(long userId, bool isSystemAdmin, long projectId, long channelId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        await GetChannelOrThrow(channelId, projectId, cancellationToken);
        await _channels.DeleteAsync(channelId, cancellationToken).ConfigureAwait(false);
    }

    public async Task TestAsync(long userId, bool isSystemAdmin, long projectId, long channelId, CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken).ConfigureAwait(false);
        var channel = await GetChannelOrThrow(channelId, projectId, cancellationToken);

        var notification = new AlertNotification
        {
            Title = "LogPilot 告警测试",
            Message = "这是一条测试通知，用于验证渠道配置是否正确。",
            Severity = AlertSeverity.P2,
            ProjectName = $"项目#{projectId}",
            RuleName = "测试规则",
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = AlertEventStatus.Firing,
            SampleLogs = Array.Empty<SampleLog>()
        };

        var result = await _dispatcher.SendToChannelAsync(channel, notification, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new AppException("NOTIFY_FAILED", $"测试发送失败：{result.ErrorMessage}");
        }
    }

    private async Task<AlertChannel> GetChannelOrThrow(long channelId, long projectId, CancellationToken cancellationToken)
    {
        var channel = await _channels.FindByIdAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null || channel.ProjectId != projectId)
        {
            throw new AppException("NOT_FOUND", "渠道不存在。", 404);
        }

        return channel;
    }

    private static void ValidateConfig(AlertChannelType type, string configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            throw new AppException("INVALID_ARGUMENT", "渠道配置不能为空。");
        }

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            switch (type)
            {
                case AlertChannelType.DingTalk:
                case AlertChannelType.WeCom:
                    if (!root.TryGetProperty("webhookUrl", out _))
                    {
                        throw new AppException("INVALID_ARGUMENT", "webhookUrl 必填。");
                    }
                    break;
                case AlertChannelType.Email:
                    if (!root.TryGetProperty("smtpHost", out _) || !root.TryGetProperty("to", out _))
                    {
                        throw new AppException("INVALID_ARGUMENT", "smtpHost 和 to 必填。");
                    }
                    break;
                case AlertChannelType.Webhook:
                    if (!root.TryGetProperty("url", out _))
                    {
                        throw new AppException("INVALID_ARGUMENT", "url 必填。");
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            throw new AppException("INVALID_ARGUMENT", "渠道配置不是合法的 JSON。");
        }
    }

    private static AlertChannelDto Map(AlertChannel c) => new()
    {
        Id = c.Id,
        ProjectId = c.ProjectId,
        Name = c.Name,
        Type = c.Type.ToString().ToLowerInvariant(),
        ConfigSummary = BuildConfigSummary(c.Type, c.ConfigJson),
        Enabled = c.Enabled == EntityStatus.Enabled,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt
    };

    private static string BuildConfigSummary(AlertChannelType type, string configJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            return type switch
            {
                AlertChannelType.DingTalk or AlertChannelType.WeCom =>
                    root.TryGetProperty("webhookUrl", out var u) ? $"webhook: {TruncateUrl(u.GetString() ?? string.Empty)}" : "未配置",
                AlertChannelType.Email =>
                    root.TryGetProperty("smtpHost", out var h) ? $"SMTP: {h.GetString()}" : "未配置",
                AlertChannelType.Webhook =>
                    root.TryGetProperty("url", out var u) ? $"url: {TruncateUrl(u.GetString() ?? string.Empty)}" : "未配置",
                _ => "未知"
            };
        }
        catch
        {
            return "解析失败";
        }
    }

    private static string TruncateUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        return url.Length > 50 ? url[..50] + "…" : url;
    }
}
