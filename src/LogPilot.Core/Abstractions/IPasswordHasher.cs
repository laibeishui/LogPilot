namespace LogPilot.Core.Abstractions;

/// <summary>
/// 用户密码哈希抽象（实现使用 ASP.NET Core PasswordHasher）。
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string hashedPassword, string providedPassword);
}
