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
}
