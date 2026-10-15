using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using LogPilot.Application.Ingestion;
using LogPilot.Application.Options;
using LogPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace LogPilot.Infrastructure.Otlp;

public sealed class OtlpLogIngestService : IOtlpLogIngestService
{
    private readonly ILogIngestionService _ingestion;
    private readonly IngestionLimits _limits;
    private readonly ILogger<OtlpLogIngestService> _logger;

    public OtlpLogIngestService(
        ILogIngestionService ingestion,
        IOptions<IngestionLimits> limits,
        ILogger<OtlpLogIngestService> logger)
    {
        _ingestion = ingestion;
        _limits = limits.Value;
        _logger = logger;
    }

    public async Task<OtlpIngestResult> IngestAsync(
        IngestPrincipal principal,
        byte[] body,
        OtlpEncoding encoding,
        CancellationToken cancellationToken = default)
    {
        if (body.Length == 0)
        {
            return Fail(StatusCodes.Status400BadRequest, "empty body", encoding);
        }

        if (body.Length > _limits.MaxBodyBytes)
        {
            return Fail(StatusCodes.Status413PayloadTooLarge, "payload too large", encoding);
        }

        ExportLogsServiceRequest request;
        try
        {
            request = ParseRequest(body, encoding);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException
                                       or JsonException
                                       or InvalidOperationException
                                       or FormatException)
        {
            _logger.LogWarning(ex, "Invalid OTLP payload (encoding={Encoding})", encoding);
            return Fail(StatusCodes.Status400BadRequest, "invalid payload", encoding);
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var logs = OtlpLogMapper.Map(request, principal.ProjectId, receivedAt);
        if (logs.Count == 0)
        {
            return Success(new ExportLogsServiceResponse(), encoding);
        }

        if (logs.Count > _limits.MaxBatchRecords)
        {
            return Fail(StatusCodes.Status413PayloadTooLarge, "too many log records", encoding);
        }

        var write = await _ingestion.IngestAsync(principal.ProjectId, logs, cancellationToken)
            .ConfigureAwait(false);

        if (!write.Succeeded)
        {
            return Fail(
                write.IsRetryable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
                write.ErrorMessage ?? "storage write failed",
                encoding);
        }

        return Success(new ExportLogsServiceResponse(), encoding);
    }

    private static ExportLogsServiceRequest ParseRequest(byte[] body, OtlpEncoding encoding)
    {
        if (encoding == OtlpEncoding.Json)
        {
            var node = JsonNode.Parse(Encoding.UTF8.GetString(body));
            ConvertHexIds(node);
            var parser = new JsonParser(JsonParser.Settings.Default.WithIgnoreUnknownFields(true));
            return parser.Parse<ExportLogsServiceRequest>(node?.ToJsonString() ?? "{}");
        }

        return ExportLogsServiceRequest.Parser.ParseFrom(body);
    }

    /// <summary>
    /// OTLP/JSON 按规范用十六进制表示 traceId/spanId，而 Protobuf JSON 映射要求 base64。
    /// 解析前把十六进制值转换为 base64，避免静默解析错误。
    /// </summary>
    private static void ConvertHexIds(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Value is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && IsHexId(pair.Key, text))
                    {
                        obj[pair.Key] = Convert.ToBase64String(Convert.FromHexString(text));
                    }
                    else
                    {
                        ConvertHexIds(pair.Value);
                    }
                }

                break;

            case JsonArray array:
                foreach (var item in array)
                {
                    ConvertHexIds(item);
                }

                break;
        }
    }

    private static bool IsHexId(string name, string value)
    {
        var isIdField = name.Equals("traceId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("spanId", StringComparison.OrdinalIgnoreCase)
            || name.Equals("trace_id", StringComparison.OrdinalIgnoreCase)
            || name.Equals("span_id", StringComparison.OrdinalIgnoreCase);

        if (!isIdField)
        {
            return false;
        }

        return value.Length is 16 or 32 && value.All(Uri.IsHexDigit);
    }

    private static OtlpIngestResult Success(ExportLogsServiceResponse response, OtlpEncoding encoding)
        => new()
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = ContentTypeFor(encoding),
            ResponseBody = EncodeResponse(response, encoding)
        };

    private static OtlpIngestResult Fail(int statusCode, string message, OtlpEncoding encoding)
    {
        // V0.1：失败时返回空响应体 + 正确 HTTP 状态；完整 google.rpc.Status 可后续增强
        _ = message;
        return new OtlpIngestResult
        {
            StatusCode = statusCode,
            ContentType = ContentTypeFor(encoding),
            ResponseBody = Array.Empty<byte>()
        };
    }

    private static byte[] EncodeResponse(ExportLogsServiceResponse response, OtlpEncoding encoding)
        => encoding == OtlpEncoding.Json
            ? Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(response))
            : response.ToByteArray();

    private static string ContentTypeFor(OtlpEncoding encoding)
        => encoding == OtlpEncoding.Json ? "application/json" : "application/x-protobuf";

    private static class StatusCodes
    {
        public const int Status200OK = 200;
        public const int Status400BadRequest = 400;
        public const int Status413PayloadTooLarge = 413;
        public const int Status500InternalServerError = 500;
        public const int Status503ServiceUnavailable = 503;
    }
}
