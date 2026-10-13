using System.Globalization;
using System.Text;
using Google.Protobuf;
using Google.Protobuf.Collections;
using LogPilot.Application.Ingestion;
using LogPilot.Core.Models;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;

namespace LogPilot.Infrastructure.Otlp;

/// <summary>
/// 将 OTLP ResourceLogs 树映射为 NormalizedLog；强制注入服务端 projectId。
/// </summary>
public static class OtlpLogMapper
{
    public static IReadOnlyList<NormalizedLog> Map(
        ExportLogsServiceRequest? request,
        long projectId,
        DateTimeOffset receivedAt)
    {
        if (request is null || request.ResourceLogs.Count == 0)
        {
            return Array.Empty<NormalizedLog>();
        }

        var result = new List<NormalizedLog>();
        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resourceAttrs = ToDictionary(resourceLogs.Resource?.Attributes);
            var serviceName = GetString(resourceAttrs, "service.name") ?? "unknown";
            var environment = GetString(resourceAttrs, "deployment.environment.name")
                ?? GetString(resourceAttrs, "deployment.environment")
                ?? string.Empty;
            var hostName = GetString(resourceAttrs, "host.name");

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                foreach (var record in scopeLogs.LogRecords)
                {
                    result.Add(MapRecord(record, projectId, serviceName, environment, hostName, receivedAt));
                }
            }
        }

        return result;
    }

    private static NormalizedLog MapRecord(
        LogRecord record,
        long projectId,
        string serviceName,
        string environment,
        string? hostName,
        DateTimeOffset receivedAt)
    {
        var timestamp = ResolveTimestamp(record, receivedAt);
        var level = !string.IsNullOrWhiteSpace(record.SeverityText)
            ? LogSeverityMapper.Normalize(record.SeverityText)
            : LogSeverityMapper.FromOtlpSeverityNumber((int)record.SeverityNumber);

        var attrs = ToDictionary(record.Attributes);
        // 忽略客户端伪造的 project 字段
        attrs.Remove("project_id");
        attrs.Remove("projectId");

        var exceptionType = GetString(attrs, "exception.type");
        var exceptionMessage = GetString(attrs, "exception.message");
        var exceptionStack = GetString(attrs, "exception.stacktrace");

        return new NormalizedLog
        {
            EventId = Guid.NewGuid().ToString("N"),
            ProjectId = projectId,
            Timestamp = timestamp,
            ReceivedAt = receivedAt,
            ServiceName = serviceName,
            Environment = environment,
            Level = level,
            Message = BodyToString(record.Body),
            TraceId = BytesToHex(record.TraceId),
            SpanId = BytesToHex(record.SpanId),
            HostName = hostName,
            ExceptionType = exceptionType,
            ExceptionMessage = exceptionMessage,
            ExceptionStack = exceptionStack,
            Attributes = attrs
        };
    }

    private static DateTimeOffset ResolveTimestamp(LogRecord record, DateTimeOffset receivedAt)
    {
        if (record.TimeUnixNano != 0)
        {
            return FromUnixNano(record.TimeUnixNano);
        }

        if (record.ObservedTimeUnixNano != 0)
        {
            return FromUnixNano(record.ObservedTimeUnixNano);
        }

        return receivedAt;
    }

    private static DateTimeOffset FromUnixNano(ulong unixNano)
    {
        // .NET DateTimeOffset ticks are 100ns; Unix epoch offset
        var ticks = (long)(unixNano / 100);
        return DateTimeOffset.UnixEpoch.AddTicks(ticks);
    }

    private static string BodyToString(AnyValue? body)
    {
        if (body is null)
        {
            return string.Empty;
        }

        return body.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => body.StringValue,
            AnyValue.ValueOneofCase.IntValue => body.IntValue.ToString(CultureInfo.InvariantCulture),
            AnyValue.ValueOneofCase.DoubleValue => body.DoubleValue.ToString(CultureInfo.InvariantCulture),
            AnyValue.ValueOneofCase.BoolValue => body.BoolValue ? "true" : "false",
            AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(body.BytesValue.ToByteArray()),
            AnyValue.ValueOneofCase.ArrayValue => body.ArrayValue.ToString(),
            AnyValue.ValueOneofCase.KvlistValue => body.KvlistValue.ToString(),
            _ => string.Empty
        };
    }

    private static Dictionary<string, object?> ToDictionary(RepeatedField<KeyValue>? attributes)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (attributes is null)
        {
            return dict;
        }

        foreach (var kv in attributes)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }

            dict[kv.Key] = AnyValueToObject(kv.Value);
        }

        return dict;
    }

    private static object? AnyValueToObject(AnyValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.ToByteArray()),
            AnyValue.ValueOneofCase.ArrayValue => value.ArrayValue.Values.Select(AnyValueToObject).ToList(),
            AnyValue.ValueOneofCase.KvlistValue => value.KvlistValue.Values
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key, x => AnyValueToObject(x.Value)),
            _ => null
        };
    }

    private static string? GetString(Dictionary<string, object?> attrs, string key)
        => attrs.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string? BytesToHex(ByteString bytes)
    {
        if (bytes is null || bytes.IsEmpty)
        {
            return null;
        }

        var data = bytes.ToByteArray();
        var sb = new StringBuilder(data.Length * 2);
        foreach (var b in data)
        {
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
