using System.Text.RegularExpressions;
using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Projects;

public sealed class ProjectService : IProjectService
{
    private static readonly Regex CodePattern = new(@"^[a-z0-9][a-z0-9\-]{1,62}[a-z0-9]$|^[a-z0-9]{2,64}$", RegexOptions.Compiled);

    private readonly IProjectRepository _projects;
    private readonly IProjectAccessService _access;

    public ProjectService(IProjectRepository projects, IProjectAccessService access)
    {
        _projects = projects;
        _access = access;
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(
        long userId,
        bool isSystemAdmin,
        CancellationToken cancellationToken = default)
    {
        var items = await _projects.ListAccessibleAsync(userId, isSystemAdmin, cancellationToken)
            .ConfigureAwait(false);
        return items.Select(Map).ToList();
    }

    public async Task<ProjectDto> GetAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var project = await _projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "项目不存在。", 404);
        return Map(project);
    }

    public async Task<ProjectDto> CreateAsync(
        long userId,
        bool isSystemAdmin,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        // V0.1：系统管理员或已登录用户均可创建；创建者成为 owner
        var name = (request.Name ?? string.Empty).Trim();
        var code = (request.Code ?? string.Empty).Trim().ToLowerInvariant();
        var description = (request.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "项目名称必填且不超过 100 字符。");
        }

        if (!CodePattern.IsMatch(code))
        {
            throw new AppException("INVALID_ARGUMENT", "项目编码需为 2–64 位小写字母、数字或短横线。");
        }

        if (description.Length > 500)
        {
            throw new AppException("INVALID_ARGUMENT", "项目说明不超过 500 字符。");
        }

        var existing = await _projects.FindByCodeAsync(code, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new AppException("CONFLICT", "项目编码已存在。", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Code = code,
            Name = name,
            Description = description,
            Status = EntityStatus.Enabled,
            CreatedBy = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _projects.InsertAsync(project, cancellationToken).ConfigureAwait(false);
        await _projects.AddMemberAsync(id, userId, ProjectMemberRole.Owner, now, cancellationToken)
            .ConfigureAwait(false);

        project = (await _projects.FindByIdAsync(id, cancellationToken).ConfigureAwait(false))!;
        return Map(project);
    }

    public async Task<ProjectDto> UpdateAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var project = await _projects.FindByIdAsync(projectId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "项目不存在。", 404);

        var name = request.Name is null ? project.Name : request.Name.Trim();
        var description = request.Description is null ? project.Description : request.Description.Trim();
        var status = request.Status ?? project.Status;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "项目名称必填且不超过 100 字符。");
        }

        if (description.Length > 500)
        {
            throw new AppException("INVALID_ARGUMENT", "项目说明不超过 500 字符。");
        }

        var updated = new Project
        {
            Id = project.Id,
            Code = project.Code,
            Name = name,
            Description = description,
            Status = status,
            CreatedBy = project.CreatedBy,
            CreatedAt = project.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _projects.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return Map(updated);
    }

    private static ProjectDto Map(Project project) => new()
    {
        Id = project.Id,
        Code = project.Code,
        Name = project.Name,
        Description = project.Description,
        Status = project.Status == EntityStatus.Enabled ? "enabled" : "disabled",
        CreatedAt = project.CreatedAt,
        UpdatedAt = project.UpdatedAt
    };
}
