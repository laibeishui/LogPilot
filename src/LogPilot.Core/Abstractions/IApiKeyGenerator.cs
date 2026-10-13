namespace LogPilot.Core.Abstractions;

/// <summary>
/// 生成高熵接入密钥；明文仅在创建时返回一次。
/// </summary>
public interface IApiKeyGenerator
{
    GeneratedApiKey Create();
}

public sealed class GeneratedApiKey
{
    public required string Plaintext { get; init; }

    public required string Prefix { get; init; }

    public required string Hash { get; init; }
}
