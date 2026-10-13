namespace LogPilot.Core.Models;

/// <summary>
/// OTLP / JSON 接入归一化后的统一日志模型。
/// project_id 必须由服务端根据凭据注入，不可信任客户端自报值。
/// </summary>
public sealed class NormalizedLog
{
    public required string EventId { get; init; }

    public required long ProjectId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public required string ServiceName { get; init; }

    public string Environment { get; init; } = string.Empty;

    public required string Level { get; init; }

    public required string Message { get; init; }

    public string? TraceId { get; init; }

    public string? SpanId { get; init; }

    public string? HostName { get; init; }

    public string? ExceptionType { get; init; }

    public string? ExceptionMessage { get; init; }

    public string? ExceptionStack { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();
}
