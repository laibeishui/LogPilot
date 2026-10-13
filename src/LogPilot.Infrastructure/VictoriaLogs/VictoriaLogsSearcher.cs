using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LogPilot.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.VictoriaLogs;

/// <summary>
/// VictoriaLogs LogsQL 查询适配器。
/// </summary>
public sealed class VictoriaLogsSearcher : ILogSearcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VictoriaLogsSearcher> _logger;

    public VictoriaLogsSearcher(
        IHttpClientFactory httpClientFactory,
        ILogger<VictoriaLogsSearcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<LogSearchResult> SearchAsync(
        AuthorizedLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var effective = ApplyCursor(query);
        var logsQl = LogsQlBuilder.BuildSearch(effective);
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);

        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = rows
            .Select(MapSearchItem)
            .Where(x => x is not null)
            .Cast<LogSearchItem>()
            .ToList();

        // 游标页：去掉与锚点完全相同的事件，避免重复
        if (SearchCursor.TryDecode(query.Cursor, out var cursor) && !string.IsNullOrWhiteSpace(cursor.EventId))
        {
            items = items
                .Where(x => !(x.Timestamp == cursor.CursorTime && x.Id == cursor.EventId))
                .ToList();
        }

        var hasMore = items.Count > pageSize;
        if (hasMore)
        {
            items = items.Take(pageSize).ToList();
        }

        string? nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[^1];
            nextCursor = SearchCursor.Encode(last.Timestamp, last.Id);
        }

        return new LogSearchResult
        {
            Items = items,
            HasMore = hasMore,
            NextCursor = nextCursor
        };
    }

    public async Task<LogDetail?> GetByEventIdAsync(
        long projectId,
        string eventId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildGetByEventId(projectId, eventId, startTime, endTime);
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return null;
        }

        return MapDetail(rows[0], projectId);
    }

    public async Task<IReadOnlyList<string>> ListServicesAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildServiceFacets(projectId, startTime, endTime);
        try
        {
            var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
            return rows
                .Select(r => GetString(r, "service_name"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .Take(200)
                .ToList()!;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list services for project {ProjectId}", projectId);
            return Array.Empty<string>();
        }
    }

    private static AuthorizedLogQuery ApplyCursor(AuthorizedLogQuery query)
    {
        if (!SearchCursor.TryDecode(query.Cursor, out var cursor))
        {
            return query;
        }

        // 下一页：结束时间收敛到游标时间（半开区间上界）
        var end = cursor.CursorTime;
        if (end <= query.StartTime)
        {
            end = query.StartTime.AddMilliseconds(1);
        }

        return new AuthorizedLogQuery
        {
            ProjectId = query.ProjectId,
            StartTime = query.StartTime,
            EndTime = end,
            Services = query.Services,
            Levels = query.Levels,
            Keyword = query.Keyword,
            TraceId = query.TraceId,
            PageSize = query.PageSize,
            Cursor = query.Cursor
        };
    }

    private async Task<List<JsonElement>> ExecuteQueryAsync(string logsQl, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("VictoriaLogs");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["query"] = logsQl
        });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await client.PostAsync("select/logsql/query", content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("VictoriaLogs query failed. Status={Status} Body={Body}", (int)response.StatusCode, Truncate(body));
            throw new InvalidOperationException($"VictoriaLogs query failed with {(int)response.StatusCode}");
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseNdjson(text);
    }

    private static List<JsonElement> ParseNdjson(string text)
    {
        var list = new List<JsonElement>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                list.Add(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                // 跳过坏行
            }
        }

        return list;
    }

    private static LogSearchItem? MapSearchItem(JsonElement row)
    {
        var message = GetString(row, "_msg") ?? GetString(row, "message") ?? string.Empty;
        var service = GetString(row, "service_name") ?? "unknown";
        var level = GetString(row, "level") ?? "INFO";
        var id = GetString(row, "event_id") ?? BuildFallbackId(row);
        var timestamp = GetTimestamp(row) ?? DateTimeOffset.UtcNow;

        return new LogSearchItem
        {
            Id = id,
            Timestamp = timestamp,
            ServiceName = service,
            Level = level,
            Message = message,
            TraceId = GetString(row, "trace_id")
        };
    }

    private static LogDetail MapDetail(JsonElement row, long projectId)
    {
        var attributes = new Dictionary<string, object?>();
        foreach (var prop in row.EnumerateObject())
        {
            if (prop.Name is "_time" or "_msg" or "_stream" or "_stream_id"
                or "project_id" or "service_name" or "environment" or "level"
                or "event_id" or "trace_id" or "span_id" or "host_name"
                or "exception_type" or "exception_message" or "exception_stack"
                or "received_at")
            {
                continue;
            }

            attributes[prop.Name] = JsonValueToObject(prop.Value);
        }

        return new LogDetail
        {
            Id = GetString(row, "event_id") ?? BuildFallbackId(row),
            ProjectId = projectId,
            Timestamp = GetTimestamp(row) ?? DateTimeOffset.UtcNow,
            ReceivedAt = GetTimestampField(row, "received_at"),
            ServiceName = GetString(row, "service_name") ?? "unknown",
            Environment = GetString(row, "environment") ?? string.Empty,
            Level = GetString(row, "level") ?? "INFO",
            Message = GetString(row, "_msg") ?? string.Empty,
            TraceId = GetString(row, "trace_id"),
            SpanId = GetString(row, "span_id"),
            HostName = GetString(row, "host_name"),
            ExceptionType = GetString(row, "exception_type"),
            ExceptionMessage = GetString(row, "exception_message"),
            ExceptionStack = GetString(row, "exception_stack"),
            Attributes = attributes
        };
    }

    private static string BuildFallbackId(JsonElement row)
    {
        var ts = GetString(row, "_time") ?? "0";
        var msg = GetString(row, "_msg") ?? string.Empty;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ts + "|" + msg)))
            .ToLowerInvariant();
        return "evt_" + hash[..16];
    }

    private static DateTimeOffset? GetTimestamp(JsonElement row)
        => GetTimestampField(row, "_time");

    private static DateTimeOffset? GetTimestampField(JsonElement row, string name)
    {
        var raw = GetString(row, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.ToUniversalTime();
        }

        return null;
    }

    private static string? GetString(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => prop.ToString()
        };
    }

    private static object? JsonValueToObject(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.ToString()
        };

    private static string Truncate(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= 500 ? value : value[..500]);
}
