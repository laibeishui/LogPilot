using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Identity;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;

    public AuthService(IUserRepository users, IPasswordHasher passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<bool> NeedsSetupAsync(CancellationToken cancellationToken = default)
        => await _users.CountSystemAdminsAsync(cancellationToken).ConfigureAwait(false) == 0;

    public async Task<CurrentUserDto> SetupAdminAsync(
        SetupAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await NeedsSetupAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new AppException("ALREADY_INITIALIZED", "系统已初始化，不能重复创建管理员。", 409);
        }

        ValidateCredentials(request.Username, request.Password);

        var existing = await _users.FindByUsernameAsync(request.Username.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new AppException("CONFLICT", "用户名已存在。", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Username = request.Username.Trim(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? request.Username.Trim()
                : request.DisplayName.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Status = EntityStatus.Enabled,
            IsSystemAdmin = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _users.InsertAsync(user, cancellationToken).ConfigureAwait(false);
        return new CurrentUserDto
        {
            Id = id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsSystemAdmin = true
        };
    }

    public async Task<CurrentUserDto> ValidateLoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByUsernameAsync(request.Username.Trim(), cancellationToken)
            .ConfigureAwait(false);

        // 统一失败文案，避免枚举账号是否存在
        if (user is null
            || user.Status != EntityStatus.Enabled
            || !_passwordHasher.VerifyPassword(user.PasswordHash, request.Password))
        {
            throw new AppException("UNAUTHORIZED", "账号或密码不正确。", 401);
        }

        var now = DateTimeOffset.UtcNow;
        await _users.UpdateLastLoginAsync(user.Id, now, cancellationToken).ConfigureAwait(false);

        return new CurrentUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsSystemAdmin = user.IsSystemAdmin
        };
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != EntityStatus.Enabled)
        {
            return null;
        }

        return new CurrentUserDto
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            IsSystemAdmin = user.IsSystemAdmin
        };
    }

    private static void ValidateCredentials(string username, string password)
        => UserCredentialRules.Validate(username, password);
}
