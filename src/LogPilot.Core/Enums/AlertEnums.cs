namespace LogPilot.Core.Enums;

/// <summary>
/// 通知渠道类型。
/// </summary>
public enum AlertChannelType
{
    DingTalk = 0,
    WeCom = 1,
    Email = 2,
    Webhook = 3
}

/// <summary>
/// 告警条件类型。V0.2 先支持 count。
/// </summary>
public enum AlertConditionType
{
    Count = 0
}

/// <summary>
/// 告警严重级别。
/// </summary>
public enum AlertSeverity
{
    P0 = 0,
    P1 = 1,
    P2 = 2
}

/// <summary>
/// 告警事件状态。
/// </summary>
public enum AlertEventStatus
{
    Firing = 0,
    Acknowledged = 1,
    Resolved = 2,
    Silenced = 3
}
