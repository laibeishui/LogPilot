namespace LogPilot.Application.Ingestion;

public sealed class JsonIngestRequest
{
    public List<JsonLogItem>? Logs { get; init; }
}

public sealed class JsonLogItem
{
    public string? Timestamp { get; init; }

    public string? ServiceName { get; init; }

    public string? Environment { get; init; }

    public string? Level { get; init; }

    public string? Message { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? HostName { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? ExceptionStackTrace { get; init; }

    public string? EventId { get; init; }

    /// <summary>
    /// 客户端自报 projectId 将被忽略。
    /// </summary>
    public long? ProjectId { get; init; }

    public Dictionary<string, object?>? Attributes { get; init; }
}

public sealed class JsonIngestResponse
{
    public required string RequestId { get; init; }

    public required int Accepted { get; init; }

    public required int Rejected { get; init; }

    public required string Status { get; init; }

    public IReadOnlyList<JsonIngestError> Errors { get; init; } = Array.Empty<JsonIngestError>();
}

public sealed class JsonIngestError
{
    public required int Index { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}

public sealed class IngestPrincipal
{
    public required long ApiKeyId { get; init; }

    public required long ProjectId { get; init; }

    public required string ProjectCode { get; init; }

    public required string KeyPrefix { get; init; }
}
