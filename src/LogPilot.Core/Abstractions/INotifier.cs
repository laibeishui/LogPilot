using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Core.Abstractions;

/// <summary>
/// 通知渠道适配器接口。每种渠道实现一个。
/// </summary>
public interface INotifier
{
    AlertChannelType ChannelType { get; }

    Task<NotifyResult> SendAsync(
        AlertNotification notification,
        string configJson,
        CancellationToken cancellationToken = default);
}
