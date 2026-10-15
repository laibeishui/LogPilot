namespace LogPilot.Application.Projects;

public interface IApplicationService
{
    Task<IReadOnlyList<ApplicationDto>> ListAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default);

    Task<ApplicationDto> CreateAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CreateApplicationRequest request,
        CancellationToken cancellationToken = default);
}
