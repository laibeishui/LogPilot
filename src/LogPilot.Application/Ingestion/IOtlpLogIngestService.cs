namespace LogPilot.Application.Ingestion;

/// <summary>
/// OTLP/HTTP 请求编码：Protobuf（默认）或 JSON。
/// </summary>
public enum OtlpEncoding
{
    Protobuf,
    Json
}

public interface IOtlpLogIngestService
{
    Task<OtlpIngestResult> IngestAsync(
        IngestPrincipal principal,
        byte[] body,
        OtlpEncoding encoding,
        CancellationToken cancellationToken = default);
}

public sealed class OtlpIngestResult
{
    public required byte[] ResponseBody { get; init; }

    public required int StatusCode { get; init; }

    public required string ContentType { get; init; }
}
