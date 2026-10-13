using LogPilot.Infrastructure.Authentication;
using Xunit;

namespace LogPilot.UnitTests.Security;

public sealed class ApiKeyGeneratorTests
{
    [Fact]
    public void Create_ProducesLpPrefix_AndStableHash()
    {
        var generator = new SecureApiKeyGenerator();
        var key = generator.Create();

        Assert.StartsWith("lp_", key.Plaintext);
        Assert.StartsWith("lp_", key.Prefix);
        Assert.Equal(SecureApiKeyGenerator.Hash(key.Plaintext), key.Hash);
        Assert.NotEqual(key.Plaintext, key.Hash);
    }
}
