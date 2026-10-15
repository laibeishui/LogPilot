using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Projects;

public sealed class ApplicationService : IApplicationService
{
    private readonly IApplicationRepository _applications;
    private readonly IProjectAccessService _access;

    public ApplicationService(IApplicationRepository applications, IProjectAccessService access)
    {
        _applications = applications;
        _access = access;
    }

    public async Task<IReadOnlyList<ApplicationDto>> ListAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanViewAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var items = await _applications.ListByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return items.Select(Map).ToList();
    }

    public async Task<ApplicationDto> CreateAsync(
        long userId,
        bool isSystemAdmin,
        long projectId,
        CreateApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        await _access.EnsureCanManageAsync(userId, isSystemAdmin, projectId, cancellationToken)
            .ConfigureAwait(false);

        var serviceName = (request.ServiceName ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var description = (request.Description ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Length > 200 || !IsSafeServiceName(serviceName))
        {
            throw new AppException(
                "INVALID_ARGUMENT",
                "serviceName 必填，不超过 200 字符，且仅允许字母、数字与 . _ - / : @ 符号。");
        }

        if (displayName.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "displayName 不超过 100 字符。");
        }

        if (description.Length > 500)
        {
            throw new AppException("INVALID_ARGUMENT", "description 不超过 500 字符。");
        }

        var existing = await _applications.FindByServiceNameAsync(projectId, serviceName, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new AppException("CONFLICT", "同名服务已存在。", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var application = new ProjectApplication
        {
            ProjectId = projectId,
            ServiceName = serviceName,
            DisplayName = displayName,
            Description = description,
            Status = EntityStatus.Enabled,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _applications.InsertAsync(application, cancellationToken).ConfigureAwait(false);
        var saved = (await _applications.FindByIdAsync(id, cancellationToken).ConfigureAwait(false))!;
        return Map(saved);
    }

    private static bool IsSafeServiceName(string value)
    {
        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '/' or ':' or '@'))
            {
                return false;
            }
        }

        return true;
    }

    private static ApplicationDto Map(ProjectApplication application) => new()
    {
        Id = application.Id,
        ProjectId = application.ProjectId,
        ServiceName = application.ServiceName,
        DisplayName = application.DisplayName,
        Description = application.Description,
        Status = application.Status == EntityStatus.Enabled ? "enabled" : "disabled",
        CreatedAt = application.CreatedAt,
        UpdatedAt = application.UpdatedAt
    };
}
