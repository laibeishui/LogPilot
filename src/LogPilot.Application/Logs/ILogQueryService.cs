namespace LogPilot.Application.Logs;

/// <summary>
/// 控制台日志查询用例：鉴权、时间窗校验后调用 VictoriaLogs 适配器。
/// </summary>
public interface ILogQueryService
{
    Task<LogSearchResponseDto> SearchAsync(
        long userId,
        bool isSystemAdmin,
        LogSearchRequestDto request,
        CancellationToken cancellationToken = default);

    Task<LogDetailDto> GetDetailAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        string eventId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default);

    Task<LogFiltersDto> GetFiltersAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default);

    Task<LogContextDto> GetContextAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? timestamp,
        int? beforeMinutes,
        int? afterMinutes,
        int? limit,
        CancellationToken cancellationToken = default);

    Task<StatsOverviewDto> GetOverviewAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        CancellationToken cancellationToken = default);

    Task<StatsTrendDto> GetTrendAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        string? interval,
        CancellationToken cancellationToken = default);

    Task<StatsExceptionsDto> GetExceptionsAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        DateTimeOffset? startTime,
        DateTimeOffset? endTime,
        int? top,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 实时跟随日志：逐条产出新日志，用于 SSE 推送。
    /// </summary>
    IAsyncEnumerable<LogSearchItemDto> TailAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        string? service,
        string? level,
        string? keyword,
        CancellationToken cancellationToken = default);
}
