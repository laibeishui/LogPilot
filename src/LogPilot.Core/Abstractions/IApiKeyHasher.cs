namespace LogPilot.Core.Abstractions;

public interface IApiKeyHasher
{
    string Hash(string plaintext);
}
