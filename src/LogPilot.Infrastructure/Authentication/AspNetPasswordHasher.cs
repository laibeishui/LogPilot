using LogPilot.Core.Abstractions;
using LogPilot.Core.Models;
using Microsoft.AspNetCore.Identity;

namespace LogPilot.Infrastructure.Authentication;

/// <summary>
/// 使用 ASP.NET Core PasswordHasher&lt;TUser&gt;，禁止自制 SHA-256。
/// </summary>
public sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public string HashPassword(string password)
        => _hasher.HashPassword(user: null!, password);

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        var result = _hasher.VerifyHashedPassword(user: null!, hashedPassword, providedPassword);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
