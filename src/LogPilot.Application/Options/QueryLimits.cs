namespace LogPilot.Application.Options;

public sealed class QueryLimits
{
    public const string SectionName = "Query";

    /// <summary>
    /// 普通查询最大时间跨度（小时）。
    /// </summary>
    public int MaxRangeHours { get; set; } = 24;

    public int DefaultRangeMinutes { get; set; } = 15;

    public int DefaultPageSize { get; set; } = 50;

    public int MaxPageSize { get; set; } = 100;

    public int QueryTimeoutSeconds { get; set; } = 15;
}
