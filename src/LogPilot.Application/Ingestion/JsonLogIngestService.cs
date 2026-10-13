using System.Text;
using System.Text.Json;
using LogPilot.Application.Common;
using LogPilot.Application.Options;
using LogPilot.Core.Models;
using Microsoft.Extensions.Options;

namespace LogPilot.Application.Ingestion;

public sealed class JsonLogIngestService : IJsonLogIngestService
{
    private readonly ILogIngestionService _ingestion;
    private readonly IngestionLimits _limits;

    public JsonLogIngestService(ILogIngestionService ingestion, IOptions<IngestionLimits> limits)
    {
        _ingestion = ingestion;
        _limits = limits.Value;
    }

    public async Task<JsonIngestResponse> IngestAsync(
        IngestPrincipal principal,
        JsonIngestRequest request,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        if (request.Logs is null || request.Logs.Count == 0)
        {
            throw new AppException("INVALID_ARGUMENT", "logs 不能为空。");
        }

        if (request.Logs.Count > _limits.MaxBatchRecords)
        {
            throw new AppException("PAYLOAD_TOO_LARGE", $"单批最多 {_limits.MaxBatchRecords} 条。", 413);
        }

        var receivedAt = DateTimeOffset.UtcNow;
        var accepted = new List<NormalizedLog>();
        var errors = new List<JsonIngestError>();

        for (var i = 0; i < request.Logs.Count; i++)
        {
            try
            {
                accepted.Add(Normalize(principal.ProjectId, request.Logs[i], receivedAt));
            }
            catch (AppException ex)
            {
                errors.Add(new JsonIngestError
                {
                    Index = i,
                    Code = ex.Code,
                    Message = ex.Message
                });
            }
        }

        if (accepted.Count == 0)
        {
            return new JsonIngestResponse
            {
                RequestId = requestId,
                Accepted = 0,
                Rejected = errors.Count,
                Status = "rejected",
                Errors = errors
            };
        }

        var write = await _ingestion.IngestAsync(principal.ProjectId, accepted, cancellationToken)
            .ConfigureAwait(false);

        if (!write.Succeeded)
        {
            throw new AppException(
                write.IsRetryable ? "STORAGE_UNAVAILABLE" : "INTERNAL_ERROR",
                write.ErrorMessage ?? "写入日志存储失败。",
                write.IsRetryable ? 503 : 500);
        }

        return new JsonIngestResponse
        {
            RequestId = requestId,
            Accepted = write.AcceptedCount,
            Rejected = errors.Count,
            Status = errors.Count == 0 ? "accepted" : "partial",
            Errors = errors
        };
    }

    private NormalizedLog Normalize(long projectId, JsonLogItem item, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(item.Message))
        {
            throw new AppException("INVALID_ARGUMENT", "message 必填。");
        }

        if (Encoding.UTF8.GetByteCount(item.Message) > _limits.MaxRecordBytes)
        {
            throw new AppException("INVALID_ARGUMENT", $"单条消息超过 {_limits.MaxRecordBytes} 字节。");
        }

        var serviceName = string.IsNullOrWhiteSpace(item.ServiceName) ? "unknown" : item.ServiceName.Trim();
        if (serviceName.Length > 200)
        {
            throw new AppException("INVALID_ARGUMENT", "serviceName 过长。");
        }

        DateTimeOffset timestamp;
        if (string.IsNullOrWhiteSpace(item.Timestamp))
        {
            timestamp = receivedAt;
        }
        else if (!DateTimeOffset.TryParse(item.Timestamp, out timestamp))
        {
            throw new AppException("INVALID_ARGUMENT", "timestamp 格式无效，需 ISO-8601。");
        }

        var attributes = NormalizeAttributes(item.Attributes);

        return new NormalizedLog
        {
            EventId = string.IsNullOrWhiteSpace(item.EventId)
                ? Guid.NewGuid().ToString("N")
                : item.EventId.Trim(),
            ProjectId = projectId, // 忽略客户端 ProjectId
            Timestamp = timestamp.ToUniversalTime(),
            ReceivedAt = receivedAt,
            ServiceName = serviceName,
            Environment = (item.Environment ?? string.Empty).Trim(),
            Level = LogSeverityMapper.Normalize(item.Level),
            Message = item.Message,
            TraceId = NormalizeHexId(item.TraceId),
            SpanId = NormalizeHexId(item.SpanId),
            HostName = string.IsNullOrWhiteSpace(item.HostName) ? null : item.HostName.Trim(),
            ExceptionType = string.IsNullOrWhiteSpace(item.ExceptionType) ? null : item.ExceptionType.Trim(),
            ExceptionMessage = string.IsNullOrWhiteSpace(item.ExceptionMessage) ? null : item.ExceptionMessage.Trim(),
            ExceptionStack = string.IsNullOrWhiteSpace(item.ExceptionStackTrace) ? null : item.ExceptionStackTrace,
            Attributes = attributes
        };
    }

    private IReadOnlyDictionary<string, object?> NormalizeAttributes(Dictionary<string, object?>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        if (attributes.Count > _limits.MaxAttributes)
        {
            throw new AppException("INVALID_ARGUMENT", $"attributes 最多 {_limits.MaxAttributes} 个。");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
            {
                throw new AppException("INVALID_ARGUMENT", "attribute 键无效。");
            }

            // 禁止客户端通过 attributes 覆盖项目隔离字段
            if (key.Equals("project_id", StringComparison.OrdinalIgnoreCase)
                || key.Equals("projectId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result[key] = SanitizeAttributeValue(value, depth: 0);
        }

        return result;
    }

    private object? SanitizeAttributeValue(object? value, int depth)
    {
        if (depth > _limits.MaxAttributeDepth)
        {
            throw new AppException("INVALID_ARGUMENT", "attributes 嵌套过深。");
        }

        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(x => SanitizeAttributeValue(x, depth + 1)).ToList(),
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(x => x.Name, x => SanitizeAttributeValue(x.Value, depth + 1)),
                _ => element.ToString()
            };
        }

        return value switch
        {
            string or bool or byte or short or int or long or float or double or decimal => value,
            IEnumerable<object?> list => list.Select(x => SanitizeAttributeValue(x, depth + 1)).ToList(),
            _ => value.ToString()
        };
    }

    private static string? NormalizeHexId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant();
    }
}
