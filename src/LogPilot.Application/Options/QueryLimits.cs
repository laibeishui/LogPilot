namespace LogPilot.Application.Options;

public sealed class QueryLimits
{
    public const string SectionName = "Query";

    public int DefaultRangeMinutes { get; set; } = 15;

    public int DefaultPageSize { get; set; } = 50;

    public int MaxPageSize { get; set; } = 100;

    public int QueryTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// 实时日志（SSE）长连接期间重新校验项目权限的间隔（秒）。
    /// 用于确保成员被移除项目后，已建立的连接无法继续读取该项目日志。
    /// </summary>
    public int StreamRecheckSeconds { get; set; } = 30;
}
