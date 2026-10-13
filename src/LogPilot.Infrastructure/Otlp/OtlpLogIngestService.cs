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
        byte[] protobufBody,
        CancellationToken cancellationToken = default)
    {
        if (protobufBody.Length == 0)
        {
            return Fail(StatusCodes.Status400BadRequest, "empty body");
        }

        if (protobufBody.Length > _limits.MaxBodyBytes)
        {
            return Fail(StatusCodes.Status413PayloadTooLarge, "payload too large");
        }

        ExportLogsServiceRequest request;
        try
        {
            request = ExportLogsServiceRequest.Parser.ParseFrom(protobufBody);
        }
        catch (InvalidProtocolBufferException ex)
        {
            _logger.LogWarning(ex, "Invalid OTLP protobuf payload");
            return Fail(StatusCodes.Status400BadRequest, "invalid protobuf");
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var logs = OtlpLogMapper.Map(request, principal.ProjectId, receivedAt);
        if (logs.Count == 0)
        {
            var empty = new ExportLogsServiceResponse();
            return Success(empty);
        }

        if (logs.Count > _limits.MaxBatchRecords)
        {
            return Fail(StatusCodes.Status413PayloadTooLarge, "too many log records");
        }

        var write = await _ingestion.IngestAsync(principal.ProjectId, logs, cancellationToken)
            .ConfigureAwait(false);

        if (!write.Succeeded)
        {
            return Fail(
                write.IsRetryable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError,
                write.ErrorMessage ?? "storage write failed");
        }

        return Success(new ExportLogsServiceResponse());
    }

    private static OtlpIngestResult Success(ExportLogsServiceResponse response)
        => new()
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/x-protobuf",
            ResponseBody = response.ToByteArray()
        };

    private static OtlpIngestResult Fail(int statusCode, string message)
    {
        // V0.1：失败时返回空 Protobuf 响应体 + 正确 HTTP 状态；完整 google.rpc.Status 可后续增强
        _ = message;
        return new OtlpIngestResult
        {
            StatusCode = statusCode,
            ContentType = "application/x-protobuf",
            ResponseBody = Array.Empty<byte>()
        };
    }

    private static class StatusCodes
    {
        public const int Status200OK = 200;
        public const int Status400BadRequest = 400;
        public const int Status413PayloadTooLarge = 413;
        public const int Status500InternalServerError = 500;
        public const int Status503ServiceUnavailable = 503;
    }
}
