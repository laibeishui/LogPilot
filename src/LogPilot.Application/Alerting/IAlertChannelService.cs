namespace LogPilot.Application.Alerting;

public interface IAlertChannelService
{
    Task<IReadOnlyList<AlertChannelDto>> ListAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task<AlertChannelDto> CreateAsync(long userId, bool isSystemAdmin, long projectId, CreateAlertChannelRequest request, CancellationToken cancellationToken = default);

    Task<AlertChannelDto> UpdateAsync(long userId, bool isSystemAdmin, long projectId, long channelId, UpdateAlertChannelRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(long userId, bool isSystemAdmin, long projectId, long channelId, CancellationToken cancellationToken = default);

    Task TestAsync(long userId, bool isSystemAdmin, long projectId, long channelId, CancellationToken cancellationToken = default);
}
