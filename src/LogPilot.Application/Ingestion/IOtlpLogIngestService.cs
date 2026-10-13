namespace LogPilot.Application.Ingestion;

public interface IOtlpLogIngestService
{
    Task<OtlpIngestResult> IngestAsync(
        IngestPrincipal principal,
        byte[] protobufBody,
        CancellationToken cancellationToken = default);
}

public sealed class OtlpIngestResult
{
    public required byte[] ResponseBody { get; init; }

    public required int StatusCode { get; init; }

    public required string ContentType { get; init; }
}
