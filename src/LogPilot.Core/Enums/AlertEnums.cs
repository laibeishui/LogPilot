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
/// 告警条件类型。
/// Count：匹配日志条数；Ratio：匹配日志占同范围全部日志的百分比。
/// </summary>
public enum AlertConditionType
{
    Count = 0,
    Ratio = 1
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
