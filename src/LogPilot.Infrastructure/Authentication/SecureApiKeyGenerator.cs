using System.Security.Cryptography;
using System.Text;
using LogPilot.Core.Abstractions;

namespace LogPilot.Infrastructure.Authentication;

/// <summary>
/// 生成 lp_ + 32 字节以上随机密钥；仅持久化 SHA-256 哈希与前缀。
/// </summary>
public sealed class SecureApiKeyGenerator : IApiKeyGenerator, IApiKeyHasher
{
    string IApiKeyHasher.Hash(string plaintext) => Hash(plaintext);

    public GeneratedApiKey Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var plaintext = "lp_" + Base64UrlEncode(bytes);
        var prefix = plaintext.Length >= 11 ? plaintext[..11] : plaintext;
        var hash = Hash(plaintext);

        return new GeneratedApiKey
        {
            Plaintext = plaintext,
            Prefix = prefix,
            Hash = hash
        };
    }

    public static string Hash(string plaintext)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
