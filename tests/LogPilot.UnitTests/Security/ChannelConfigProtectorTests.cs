using System.Security.Cryptography;
using LogPilot.Infrastructure.Security;
using Xunit;

namespace LogPilot.UnitTests.Security;

/// <summary>
/// 渠道配置加密密钥的加载与兼容性：
/// 必须同时支持十六进制与 Base64；配置非法时回退但状态可观测；
/// 且更换密钥后仍能解密历史密文，避免升级导致渠道配置不可读。
/// </summary>
public sealed class ChannelConfigProtectorTests
{
    private const string HexKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void EncryptDecrypt_RoundTrips_WithHexKey()
    {
        var protector = new ChannelConfigProtector(HexKey);

        Assert.Equal(ChannelKeyStatus.Configured, protector.KeyStatus);
        Assert.Equal("{\"secret\":\"dingtalk\"}", protector.Decrypt(protector.Encrypt("{\"secret\":\"dingtalk\"}")));
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips_WithBase64Key()
    {
        // 与 HexKey 相同的 32 字节，但按 Base64 编码（.env.example 中描述的写法）
        var base64Key = Convert.ToBase64String(Convert.FromHexString(HexKey));
        var protector = new ChannelConfigProtector(base64Key);

        Assert.Equal(ChannelKeyStatus.Configured, protector.KeyStatus);
        Assert.Equal("p@ssw0rd", protector.Decrypt(protector.Encrypt("p@ssw0rd")));
    }

    [Fact]
    public void HexAndBase64Keys_ProduceInterchangeableCiphertext()
    {
        var base64Key = Convert.ToBase64String(Convert.FromHexString(HexKey));
        var fromHex = new ChannelConfigProtector(HexKey);
        var fromBase64 = new ChannelConfigProtector(base64Key);

        // 同一密钥的两种写法必须等价，否则更换配置写法会导致历史密文无法解密
        Assert.Equal("value", fromBase64.Decrypt(fromHex.Encrypt("value")));
    }

    [Fact]
    public void Constructor_UsesDefaultKey_WhenNotConfigured()
    {
        var protector = new ChannelConfigProtector(null);

        Assert.Equal(ChannelKeyStatus.NotConfigured, protector.KeyStatus);
        Assert.Null(protector.KeyErrorMessage);
        Assert.Equal("value", protector.Decrypt(protector.Encrypt("value")));
    }

    [Fact]
    public void Constructor_FallsBackToDefaultKey_WhenKeyFormatIsInvalid()
    {
        // 配置了却写错时不崩溃（否则日志中心会整体不可用），但必须能被启动日志发现
        foreach (var invalid in new[] { "not-a-valid-key", new string('z', 64) })
        {
            var protector = new ChannelConfigProtector(invalid);

            Assert.Equal(ChannelKeyStatus.Invalid, protector.KeyStatus);
            Assert.NotNull(protector.KeyErrorMessage);
            Assert.Equal("value", protector.Decrypt(protector.Encrypt("value")));
        }
    }

    [Fact]
    public void Decrypt_ReadsLegacyCiphertextEncryptedWithDefaultKey()
    {
        // 旧版本把 Base64 写法的密钥静默忽略，期间保存的渠道配置是用默认密钥加密的。
        // 配置上真实密钥后，这些历史配置必须依然可读。
        var legacyCipher = new ChannelConfigProtector(null).Encrypt("{\"secret\":\"legacy\"}");

        var protector = new ChannelConfigProtector(HexKey);
        Assert.Equal("{\"secret\":\"legacy\"}", protector.Decrypt(legacyCipher));
    }

    [Fact]
    public void Decrypt_Throws_WhenNoCandidateKeyMatches()
    {
        var otherCipher = new ChannelConfigProtector(new string('a', 64)).Encrypt("value");

        var exception = Assert.Throws<CryptographicException>(() => new ChannelConfigProtector(HexKey).Decrypt(otherCipher));
        Assert.Contains("Alerting:ConfigKey", exception.Message);
    }

    [Fact]
    public void Decrypt_Throws_WhenCipherTextIsNotBase64()
    {
        Assert.Throws<CryptographicException>(() => new ChannelConfigProtector(HexKey).Decrypt("!!!not-base64!!!"));
    }

    [Fact]
    public void EncryptDecrypt_PreservesEmptyInput()
    {
        var protector = new ChannelConfigProtector(HexKey);

        Assert.Equal(string.Empty, protector.Encrypt(string.Empty));
        Assert.Equal(string.Empty, protector.Decrypt(string.Empty));
    }
}
