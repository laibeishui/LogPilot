namespace LogPilot.Application.Identity;

public interface IAuthService
{
    Task<bool> NeedsSetupAsync(CancellationToken cancellationToken = default);

    Task<CurrentUserDto> SetupAdminAsync(SetupAdminRequest request, CancellationToken cancellationToken = default);

    Task<CurrentUserDto> ValidateLoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<CurrentUserDto?> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default);
}
