using System.Security.Cryptography;
using System.Text;

namespace LogPilot.Infrastructure.Security;

/// <summary>
/// Alerting:ConfigKey 的加载状态。
/// </summary>
public enum ChannelKeyStatus
{
    /// <summary>已配置且格式合法。</summary>
    Configured,

    /// <summary>未配置，使用开发默认密钥。</summary>
    NotConfigured,

    /// <summary>配置了但格式非法，已回退到开发默认密钥。</summary>
    Invalid
}

/// <summary>
/// 渠道配置 AES 加密。密钥从 Alerting:ConfigKey 读取，支持 64 位十六进制或 Base64 编码的 32 字节密钥；
/// 未配置或格式非法时使用固定默认密钥（仅开发），并在启动日志中给出提示。
/// 解密时会额外兜底尝试默认密钥，确保升级/更换密钥后历史渠道配置依然可读。
/// </summary>
public sealed class ChannelConfigProtector
{
    /// <summary>开发默认密钥，仅在未配置或配置非法时使用。</summary>
    private static readonly byte[] DefaultKey =
        SHA256.HashData(Encoding.UTF8.GetBytes("LogPilotAlertingDefaultKeyV1"));

    /// <summary>AES-CBC 的初始向量长度（字节）。</summary>
    private const int IvLength = 16;

    /// <summary>加密使用的密钥。</summary>
    private readonly byte[] _key;

    /// <summary>
    /// 解密候选密钥，按优先级排列（当前密钥在前，默认密钥兜底在后）。
    /// 加密永远只使用 <see cref="_key"/>，兜底仅用于解密，因此密文会在下一次保存时自动迁移到当前密钥。
    /// </summary>
    private readonly byte[][] _decryptionKeys;

    /// <summary>密钥加载状态，供启动时输出相应级别的日志。</summary>
    public ChannelKeyStatus KeyStatus { get; }

    /// <summary><see cref="ChannelKeyStatus.Invalid"/> 时的具体原因，其余状态为 null。</summary>
    public string? KeyErrorMessage { get; }

    public ChannelConfigProtector(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            _key = DefaultKey;
            _decryptionKeys = [DefaultKey];
            KeyStatus = ChannelKeyStatus.NotConfigured;
            return;
        }

        if (TryParseKey(key.Trim(), out var parsedKey, out var error))
        {
            _key = parsedKey;
            KeyStatus = ChannelKeyStatus.Configured;
        }
        else
        {
            // 配置了却写错时不让进程崩溃：日志中心的首要职责是持续接收日志，
            // 因一个环境变量拼写错误而拒绝启动，代价远大于暂时回退到默认密钥。
            // 但绝不能静默——启动日志会以 Error 级别暴露该问题。
            _key = DefaultKey;
            KeyErrorMessage = error;
            KeyStatus = ChannelKeyStatus.Invalid;
        }

        // 旧版本只识别 64 位十六进制，若把密钥写成 Base64 会被静默忽略，
        // 期间保存的渠道配置实际是用默认密钥加密的。保留默认密钥作为解密兜底，
        // 可保证这类历史数据升级后依然可读。
        _decryptionKeys = _key.AsSpan().SequenceEqual(DefaultKey)
            ? [_key]
            : [_key, DefaultKey];
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

    /// <summary>
    /// 依次尝试各候选密钥解密。任一密钥解密成功即返回，全部失败才抛出异常。
    /// </summary>
    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return cipherText;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("渠道配置密文不是合法的 Base64 编码。", ex);
        }

        if (data.Length <= IvLength)
        {
            throw new CryptographicException("渠道配置密文长度不足，无法解密。");
        }

        foreach (var candidate in _decryptionKeys)
        {
            if (TryDecrypt(data, candidate, out var plainText))
            {
                return plainText;
            }
        }

        throw new CryptographicException(
            "渠道配置解密失败：当前 Alerting:ConfigKey 与该配置加密时使用的密钥不一致，请在告警渠道页面重新保存该渠道配置。");
    }

    private static bool TryDecrypt(byte[] data, byte[] key, out string plainText)
    {
        plainText = string.Empty;

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = data.AsSpan(0, IvLength).ToArray();

        try
        {
            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(data, IvLength, data.Length - IvLength);
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }
        catch (CryptographicException)
        {
            // 密钥不匹配时 AES 会因填充校验失败抛异常，视为“该密钥不是加密时使用的密钥”，继续尝试下一个。
            return false;
        }
    }

    /// <summary>
    /// 解析密钥：接受 64 位十六进制，或 Base64 编码的 32 字节。解析失败时返回原因，由调用方决定如何处置。
    /// </summary>
    private static bool TryParseKey(string key, out byte[] parsed, out string? error)
    {
        if (key.Length == 64 && IsHex(key))
        {
            parsed = Convert.FromHexString(key);
            error = null;
            return true;
        }

        if (TryDecodeBase64(key, out parsed))
        {
            error = null;
            return true;
        }

        parsed = [];
        error = "需要 64 位十六进制字符串，或 Base64 编码的 32 字节密钥。";
        return false;
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryDecodeBase64(string value, out byte[] key)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written == 32)
        {
            key = buffer.AsSpan(0, written).ToArray();
            return true;
        }

        key = [];
        return false;
    }
}
