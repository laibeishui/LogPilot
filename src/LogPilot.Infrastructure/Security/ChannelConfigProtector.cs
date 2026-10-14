using System.Security.Cryptography;
using System.Text;

namespace LogPilot.Infrastructure.Security;

/// <summary>
/// 渠道配置 AES 加密。密钥从 Alerting:ConfigKey 读取（32 字节 hex）；未配置时用固定默认密钥（仅开发）。
/// </summary>
public sealed class ChannelConfigProtector
{
    private readonly byte[] _key;

    public ChannelConfigProtector(string? hexKey)
    {
        _key = ParseKey(hexKey);
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return plainText;
        }

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        var data = Convert.FromBase64String(cipherText);
        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[data.Length - iv.Length];
        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(data, iv.Length, cipher, 0, cipher.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] ParseKey(string? hexKey)
    {
        if (string.IsNullOrWhiteSpace(hexKey) || hexKey.Length < 64)
        {
            // 开发默认密钥（生产必须配置）
            return SHA256.HashData(Encoding.UTF8.GetBytes("LogPilotAlertingDefaultKeyV1"));
        }

        var bytes = new byte[32];
        for (var i = 0; i < 32; i++)
        {
            bytes[i] = Convert.ToByte(hexKey.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
