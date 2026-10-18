using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;

namespace LogPilot.Application.Identity;

public sealed class UserManagementService : IUserManagementService
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;

    public UserManagementService(IUserRepository users, IPasswordHasher passwordHasher)
    {
        _users = users;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserListResult> ListAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var (items, total) = await _users
            .ListAsync(keyword?.Trim(), (page - 1) * pageSize, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return new UserListResult
        {
            Items = items.Select(Map).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var username = (request.Username ?? string.Empty).Trim();
        UserCredentialRules.Validate(username, request.Password);

        if (await _users.UsernameExistsAsync(username, null, cancellationToken).ConfigureAwait(false))
        {
            throw new AppException("CONFLICT", "用户名已存在。", 409);
        }

        var now = DateTimeOffset.UtcNow;
        var user = new UserAccount
        {
            Username = username,
            DisplayName = NormalizeDisplayName(request.DisplayName, username),
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Status = EntityStatus.Enabled,
            IsSystemAdmin = request.IsSystemAdmin,
            CreatedAt = now,
            UpdatedAt = now
        };

        var id = await _users.InsertAsync(user, cancellationToken).ConfigureAwait(false);
        var saved = await _users.FindByIdAsync(id, cancellationToken).ConfigureAwait(false) ?? user;
        return Map(saved);
    }

    public async Task<UserDto> UpdateAsync(
        long operatorUserId,
        long userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "用户不存在。", 404);

        var isSystemAdmin = request.IsSystemAdmin ?? user.IsSystemAdmin;
        var status = request.Enabled is null
            ? user.Status
            : request.Enabled.Value ? EntityStatus.Enabled : EntityStatus.Disabled;

        // 防自锁：不能停用当前登录账号；不能取消/停用最后一个启用的系统管理员。
        if (status == EntityStatus.Disabled && operatorUserId == userId)
        {
            throw new AppException("CONFLICT", "不能停用当前登录的账号。", 409);
        }

        var losingAdmin = (user.IsSystemAdmin && user.Status == EntityStatus.Enabled)
            && (!isSystemAdmin || status == EntityStatus.Disabled);
        if (losingAdmin
            && await _users.CountSystemAdminsAsync(cancellationToken).ConfigureAwait(false) <= 1)
        {
            throw new AppException("CONFLICT", "系统至少需保留一个启用状态的系统管理员。", 409);
        }

        var updated = new UserAccount
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = NormalizeDisplayName(request.DisplayName ?? user.DisplayName, user.Username),
            PasswordHash = user.PasswordHash,
            Status = status,
            IsSystemAdmin = isSystemAdmin,
            CreatedAt = user.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = user.LastLoginAt
        };

        await _users.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return Map(updated);
    }

    public async Task ResetPasswordAsync(
        long userId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        UserCredentialRules.ValidatePassword(request.Password);

        var user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("NOT_FOUND", "用户不存在。", 404);

        var updated = new UserAccount
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Status = user.Status,
            IsSystemAdmin = user.IsSystemAdmin,
            CreatedAt = user.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = user.LastLoginAt
        };

        await _users.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeOwnPasswordAsync(
        long userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        UserCredentialRules.ValidatePassword(request.NewPassword);

        var user = await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new AppException("UNAUTHORIZED", "账号不存在或已停用。", 401);

        if (user.Status != EntityStatus.Enabled
            || !_passwordHasher.VerifyPassword(user.PasswordHash, request.CurrentPassword ?? string.Empty))
        {
            throw new AppException("INVALID_CREDENTIALS", "原密码不正确。", 400);
        }

        var updated = new UserAccount
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            PasswordHash = _passwordHasher.HashPassword(request.NewPassword),
            Status = user.Status,
            IsSystemAdmin = user.IsSystemAdmin,
            CreatedAt = user.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastLoginAt = user.LastLoginAt
        };

        await _users.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeDisplayName(string? displayName, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? fallback : displayName.Trim();
        if (value.Length > 100)
        {
            throw new AppException("INVALID_ARGUMENT", "显示名不超过 100 字符。");
        }

        return value;
    }

    private static UserDto Map(UserAccount user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        DisplayName = user.DisplayName,
        Status = user.Status == EntityStatus.Enabled ? "enabled" : "disabled",
        IsSystemAdmin = user.IsSystemAdmin,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
        LastLoginAt = user.LastLoginAt
    };
}
