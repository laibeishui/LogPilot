namespace LogPilot.Application.Projects;

public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> ListAsync(long userId, bool isSystemAdmin, CancellationToken cancellationToken = default);

    Task<ProjectDto> GetAsync(long userId, bool isSystemAdmin, long projectId, CancellationToken cancellationToken = default);

    Task<ProjectDto> CreateAsync(long userId, bool isSystemAdmin, CreateProjectRequest request, CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateAsync(long userId, bool isSystemAdmin, long projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default);
}
