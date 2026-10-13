namespace LogPilot.Application.Ingestion;

public interface IIngestApiKeyAuthenticator
{
    Task<IngestPrincipal> AuthenticateAsync(string? authorizationHeader, CancellationToken cancellationToken = default);
}
