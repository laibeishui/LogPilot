namespace LogPilot.Application.Ingestion;

public interface IJsonLogIngestService
{
    Task<JsonIngestResponse> IngestAsync(
        IngestPrincipal principal,
        JsonIngestRequest request,
        string requestId,
        CancellationToken cancellationToken = default);
}
