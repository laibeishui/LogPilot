using System.Globalization;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LogPilot.Application.Common;
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
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var normalized = new AuthorizedLogQuery
        {
            ProjectId = query.ProjectId,
            StartTime = query.StartTime,
            EndTime = query.EndTime,
            Services = query.Services,
            Levels = query.Levels,
            Keyword = query.Keyword,
            TraceId = query.TraceId,
            PageSize = pageSize,
            Offset = Math.Max(0, query.Offset)
        };

        var rows = await ExecuteQueryAsync(LogsQlBuilder.BuildSearch(normalized), cancellationToken)
            .ConfigureAwait(false);

        var items = rows
            .Select(MapSearchItem)
            .Where(x => x is not null)
            .Cast<LogSearchItem>()
            .Take(pageSize)
            .ToList();

        var total = await CountSearchAsync(normalized, cancellationToken).ConfigureAwait(false);

        return new LogSearchResult
        {
            Items = items,
            Total = total
        };
    }

    /// <summary>
    /// 统计与检索完全相同的筛选条件下的日志总数。
    /// 查询失败时抛出异常，不返回 0，避免把存储故障表现成“没有日志”。
    /// </summary>
    private async Task<long> CountSearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken)
    {
        var rows = await ExecuteQueryAsync(LogsQlBuilder.BuildSearchCount(query), cancellationToken)
            .ConfigureAwait(false);
        return rows.Count == 0 ? 0 : ExtractCount(rows[0]);
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
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
        return rows
            .Select(r => GetString(r, "service_name"))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .Take(200)
            .ToList()!;
    }

    public async Task<long> CountAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string? serviceName,
        string? environment,
        IReadOnlyList<string> levels,
        string? keyword,
        string? exceptionType,
        CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildCount(projectId, startTime, endTime, serviceName, environment, levels, keyword, exceptionType);
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return 0;
        }

        // stats count() 返回 {"count()": N} 或 {"count(*)": N} 或 {"count": N}
        var row = rows[0];
        if (row.TryGetProperty("count(*)", out var countStarProp))
        {
            return ParseCount(countStarProp);
        }

        if (row.TryGetProperty("count()", out var countProp))
        {
            return ParseCount(countProp);
        }

        if (row.TryGetProperty("count", out countProp))
        {
            return ParseCount(countProp);
        }

        // 兜底：取第一个数字或数字字符串属性
        foreach (var prop in row.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var v))
            {
                return v;
            }

            if (prop.Value.ValueKind == JsonValueKind.String && long.TryParse(prop.Value.GetString(), out var sv))
            {
                return sv;
            }
        }

        return 0;
    }

    public async Task<IReadOnlyList<TrendPoint>> TrendAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string interval,
        bool errorsOnly,
        CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildTrend(projectId, startTime, endTime, interval, errorsOnly);
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
        var points = new List<TrendPoint>(rows.Count);
        foreach (var row in rows)
        {
            // VictoriaLogs 返回 _time[5m] 或 _time[1m] 等字段名
            var bucketField = FindTimeBucketField(row);
            var count = ExtractCount(row);
            if (bucketField is null)
            {
                continue;
            }

            if (DateTimeOffset.TryParse(bucketField, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var bucket))
            {
                points.Add(new TrendPoint
                {
                    Bucket = bucket.ToUniversalTime(),
                    Count = count
                });
            }
        }

        return points.OrderBy(p => p.Bucket).ToList();
    }

    public async Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(
        long projectId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildErrorSamples(projectId, startTime, endTime, limit);
        var rows = await ExecuteQueryAsync(logsQl, cancellationToken).ConfigureAwait(false);
        return rows
            .Select(MapSearchItem)
            .Where(x => x is not null)
            .Cast<LogSearchItem>()
            .ToList();
    }

    public async IAsyncEnumerable<LogSearchItem> TailAsync(
        long projectId,
        IReadOnlyList<string> services,
        IReadOnlyList<string> levels,
        string? keyword,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var logsQl = LogsQlBuilder.BuildTail(projectId, services, levels, keyword);
        var client = _httpClientFactory.CreateClient("VictoriaLogs");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["query"] = logsQl
        });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var request = new HttpRequestMessage(HttpMethod.Post, "select/logsql/tail")
        {
            Content = content
        };

        // ResponseHeadersRead：仅等待响应头，之后长连接持续读取，不受 HttpClient.Timeout 限制
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "VictoriaLogs tail failed. Status={Status} Body={Body}",
                (int)response.StatusCode,
                Truncate(body));
            yield break;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            LogSearchItem? item = null;
            try
            {
                using var doc = JsonDocument.Parse(line);
                item = MapSearchItem(doc.RootElement);
            }
            catch (JsonException)
            {
                continue;
            }

            if (item is not null)
            {
                yield return item;
            }
        }
    }

    private static string? FindTimeBucketField(JsonElement row)
    {
        // VictoriaLogs stats by (_time:5m) 返回 "_time" 字段
        if (row.TryGetProperty("_time", out var timeProp))
        {
            return timeProp.ValueKind == JsonValueKind.String
                ? timeProp.GetString()
                : timeProp.ToString();
        }

        // 兜底：取第一个看起来像时间的字符串字段
        foreach (var prop in row.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(prop.Value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                return prop.Value.GetString();
            }
        }

        return null;
    }

    private static long ExtractCount(JsonElement row)
    {
        // stats count() / count(*) 返回 count() 或 count(*) 字段（值可能是字符串）
        if (row.TryGetProperty("count(*)", out var countStarProp))
        {
            return ParseCount(countStarProp);
        }

        if (row.TryGetProperty("count()", out var countProp))
        {
            return ParseCount(countProp);
        }

        if (row.TryGetProperty("count", out countProp))
        {
            return ParseCount(countProp);
        }

        foreach (var prop in row.EnumerateObject())
        {
            if (prop.Name == "_time")
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var v))
            {
                return v;
            }

            if (prop.Value.ValueKind == JsonValueKind.String && long.TryParse(prop.Value.GetString(), out var sv))
            {
                return sv;
            }
        }

        return 0;
    }

    private static long ParseCount(JsonElement prop)
    {
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v))
        {
            return v;
        }

        if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), out var sv))
        {
            return sv;
        }

        return 0;
    }

    /// <summary>
    /// 执行 LogsQL 查询。任何下游故障（网络、超时、非 2xx）都转为 LOG_STORE_UNAVAILABLE，
    /// 由调用方决定如何呈现，绝不在这一层降级成“0 条/空结果”。
    /// </summary>
    private async Task<List<JsonElement>> ExecuteQueryAsync(string logsQl, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("VictoriaLogs");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["query"] = logsQl
        });
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync("select/logsql/query", content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "VictoriaLogs query transport failure.");
            throw LogStoreErrors.Unavailable("查询请求失败");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "VictoriaLogs query timed out.");
            throw LogStoreErrors.Unavailable("查询超时");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "VictoriaLogs query failed. Status={Status} Body={Body}",
                    (int)response.StatusCode,
                    Truncate(body));
                throw LogStoreErrors.Unavailable($"查询返回 HTTP {(int)response.StatusCode}");
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseNdjson(text);
        }
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
            TraceId = GetString(row, "trace_id"),
            ExceptionType = GetString(row, "exception_type")
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
