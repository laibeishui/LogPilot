using System.Text.RegularExpressions;
using LogPilot.Application.Common;

namespace LogPilot.Application.Identity;

/// <summary>用户名与密码的统一校验规则（初始化、创建用户、重置密码共用）。</summary>
internal static class UserCredentialRules
{
    private static readonly Regex UsernamePattern = new(@"^[a-zA-Z0-9_\-.]{3,64}$", RegexOptions.Compiled);

    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    public static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || !UsernamePattern.IsMatch(username.Trim()))
        {
            throw new AppException("INVALID_ARGUMENT", "用户名需为 3–64 位字母、数字、下划线、点或短横线。");
        }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < MinPasswordLength
            || password.Length > MaxPasswordLength)
        {
            throw new AppException("INVALID_ARGUMENT", $"密码长度需为 {MinPasswordLength}–{MaxPasswordLength} 位。");
        }
    }

    public static void Validate(string username, string password)
    {
        ValidateUsername(username);
        ValidatePassword(password);
    }
}
