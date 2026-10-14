using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Alerting;

public interface INotificationDispatcher
{
    Task<NotifyResult> SendToChannelAsync(AlertChannel channel, AlertNotification notification, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotifyResult>> SendToChannelsAsync(
        IEnumerable<AlertChannel> channels,
        AlertNotification notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 通知分发：按渠道类型选择对应的 INotifier 并发送。
/// </summary>
public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyDictionary<AlertChannelType, INotifier> _notifiers;

    public NotificationDispatcher(IEnumerable<INotifier> notifiers)
    {
        _notifiers = notifiers.ToDictionary(n => n.ChannelType);
    }

    public Task<NotifyResult> SendToChannelAsync(AlertChannel channel, AlertNotification notification, CancellationToken cancellationToken = default)
    {
        if (!_notifiers.TryGetValue(channel.Type, out var notifier))
        {
            return Task.FromResult(new NotifyResult { Succeeded = false, ErrorMessage = $"不支持的渠道类型: {channel.Type}" });
        }

        return notifier.SendAsync(notification, channel.ConfigJson, cancellationToken);
    }

    public async Task<IReadOnlyList<NotifyResult>> SendToChannelsAsync(
        IEnumerable<AlertChannel> channels,
        AlertNotification notification,
        CancellationToken cancellationToken = default)
    {
        var results = new List<NotifyResult>();
        foreach (var channel in channels)
        {
            if (channel.Enabled != EntityStatus.Enabled)
            {
                continue;
            }

            var result = await SendToChannelAsync(channel, notification, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return results;
    }
}
