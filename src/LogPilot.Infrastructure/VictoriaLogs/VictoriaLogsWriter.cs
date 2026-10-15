using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Models;
using LogPilot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogPilot.Infrastructure.VictoriaLogs;

/// <summary>
/// 通过 /insert/jsonline 将归一化日志写入 VictoriaLogs。
/// 成功语义：仅在下游 HTTP 明确成功后才返回 Succeeded。
/// </summary>
public sealed class VictoriaLogsWriter : ILogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VictoriaLogsOptions _options;
    private readonly ILogWriteMetrics _metrics;
    private readonly ILogger<VictoriaLogsWriter> _logger;

    public VictoriaLogsWriter(
        IHttpClientFactory httpClientFactory,
        IOptions<StorageOptions> options,
        ILogWriteMetrics metrics,
        ILogger<VictoriaLogsWriter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.VictoriaLogs;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<LogWriteResult> WriteAsync(
        IReadOnlyList<NormalizedLog> logs,
        CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
        {
            return LogWriteResult.Success(0);
        }

        var sb = new StringBuilder(logs.Count * 256);
        foreach (var log in logs)
        {
            var payload = ToVictoriaDocument(log);
            sb.Append(JsonSerializer.Serialize(payload, JsonOptions));
            sb.Append('\n');
        }

        var client = _httpClientFactory.CreateClient("VictoriaLogs");
        var path =
            "insert/jsonline?_time_field=_time&_msg_field=_msg&_stream_fields=project_id,service_name,environment";

        using var content = new StringContent(sb.ToString(), Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/stream+json");

        try
        {
            using var response = await client.PostAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                _metrics.RecordSuccess();
                return LogWriteResult.Success(logs.Count);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "VictoriaLogs write failed. Status={Status} Body={Body}",
                (int)response.StatusCode,
                Truncate(body));

            var reason = $"VictoriaLogs returned {(int)response.StatusCode}";
            _metrics.RecordFailure(logs.Count, reason);
            var retryable = (int)response.StatusCode is 429 or >= 500;
            return LogWriteResult.Failure(reason, retryable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _metrics.RecordFailure(logs.Count, "VictoriaLogs write timeout");
            return LogWriteResult.Failure("VictoriaLogs write timeout", retryable: true);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "VictoriaLogs unreachable. BaseUrl={BaseUrl}", _options.BaseUrl);
            _metrics.RecordFailure(logs.Count, "VictoriaLogs unreachable");
            return LogWriteResult.Failure("VictoriaLogs unreachable", retryable: true);
        }
    }

    private static Dictionary<string, object?> ToVictoriaDocument(NormalizedLog log)
    {
        var doc = new Dictionary<string, object?>
        {
            ["_time"] = log.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            ["_msg"] = log.Message,
            ["project_id"] = log.ProjectId.ToString(),
            ["service_name"] = log.ServiceName,
            ["environment"] = string.IsNullOrWhiteSpace(log.Environment) ? "default" : log.Environment,
            ["level"] = log.Level,
            ["event_id"] = log.EventId,
            ["received_at"] = log.ReceivedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };

        if (!string.IsNullOrWhiteSpace(log.TraceId))
        {
            doc["trace_id"] = log.TraceId;
        }

        if (!string.IsNullOrWhiteSpace(log.SpanId))
        {
            doc["span_id"] = log.SpanId;
        }

        if (!string.IsNullOrWhiteSpace(log.HostName))
        {
            doc["host_name"] = log.HostName;
        }

        if (!string.IsNullOrWhiteSpace(log.ExceptionType))
        {
            doc["exception_type"] = log.ExceptionType;
        }

        if (!string.IsNullOrWhiteSpace(log.ExceptionMessage))
        {
            doc["exception_message"] = log.ExceptionMessage;
        }

        if (!string.IsNullOrWhiteSpace(log.ExceptionStack))
        {
            doc["exception_stack"] = log.ExceptionStack;
        }

        foreach (var (key, value) in log.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key is "_time" or "_msg" or "project_id" or "service_name" or "environment" or "level")
            {
                continue;
            }

            doc[key] = value;
        }

        return doc;
    }

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= 500 ? value : value[..500]);
}
