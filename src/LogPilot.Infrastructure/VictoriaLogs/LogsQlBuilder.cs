using System.Globalization;
using System.Text;
using LogPilot.Core.Abstractions;

namespace LogPilot.Infrastructure.VictoriaLogs;

/// <summary>
/// 受控 LogsQL 构造器：强制 project_id + 时间窗，禁止注入任意查询片段。
/// </summary>
public static class LogsQlBuilder
{
    private static readonly HashSet<string> AllowedLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"
    };

    /// <summary>关键词匹配的字段：消息正文 + 异常信息三件套。</summary>
    private static readonly string[] KeywordFields =
    {
        "_msg", "exception_message", "exception_stack", "exception_type"
    };

    private const int MaxKeywordTerms = 8;

    private const int MaxKeywordTermLength = 200;

    public static string BuildSearch(AuthorizedLogQuery query)
    {
        var sb = new StringBuilder();
        AppendSearchFilters(sb, query);

        // event_id 为次级排序键：同一毫秒内可能有多条日志，仅按 _time 排序时顺序不稳定，
        // 会导致分页出现重复或遗漏。
        sb.Append(" | sort by (_time desc, event_id)");

        // VictoriaLogs 的 offset 是独立管道（| offset N | limit M），不能写成 limit N offset M。
        if (query.Offset > 0)
        {
            sb.Append(" | offset ").Append(query.Offset.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(" | limit ")
            .Append(Math.Clamp(query.PageSize, 1, 100).ToString(CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    /// <summary>
    /// 构造与 <see cref="BuildSearch"/> 相同筛选条件下的总数统计查询。
    /// </summary>
    public static string BuildSearchCount(AuthorizedLogQuery query)
    {
        var sb = new StringBuilder();
        AppendSearchFilters(sb, query);
        sb.Append(" | stats count()");
        return sb.ToString();
    }

    /// <summary>追加时间窗与筛选条件（供检索与计数复用）。</summary>
    private static void AppendSearchFilters(StringBuilder sb, AuthorizedLogQuery query)
    {
        AppendTimeRange(sb, query.StartTime, query.EndTime);
        sb.Append(' ');
        AppendStreamFilter(sb, query.ProjectId, query.Services);

        if (query.Levels.Count > 0)
        {
            var levels = query.Levels
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(AllowedLevels.Contains)
                .Distinct()
                .ToList();

            if (levels.Count == 1)
            {
                sb.Append(" level:").Append(levels[0]);
            }
            else if (levels.Count > 1)
            {
                sb.Append(" (");
                sb.Append(string.Join(" OR ", levels.Select(l => "level:" + l)));
                sb.Append(')');
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            AppendKeywordFilter(sb, query.Keyword);
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            sb.Append(" trace_id:").Append(Quote(query.TraceId.Trim()));
        }
    }

    public static string BuildGetByEventId(long projectId, string eventId, DateTimeOffset start, DateTimeOffset end)
    {
        var sb = new StringBuilder();
        AppendTimeRange(sb, start, end);
        sb.Append(' ');
        AppendStreamFilter(sb, projectId, Array.Empty<string>());
        sb.Append(" event_id:").Append(Quote(eventId.Trim()));
        sb.Append(" | limit 1");
        return sb.ToString();
    }

    public static string BuildServiceFacets(long projectId, DateTimeOffset start, DateTimeOffset end)
    {
        var sb = new StringBuilder();
        AppendTimeRange(sb, start, end);
        sb.Append(' ');
        AppendStreamFilter(sb, projectId, Array.Empty<string>());
        sb.Append(" | fields service_name | uniq by(service_name) | limit 200");
        return sb.ToString();
    }

    /// <summary>
    /// 构造统计查询：匹配条件的日志数量。
    /// </summary>
    public static string BuildCount(
        long projectId,
        DateTimeOffset start,
        DateTimeOffset end,
        string? serviceName,
        string? environment,
        IReadOnlyList<string> levels,
        string? keyword,
        string? exceptionType)
    {
        var sb = new StringBuilder();
        AppendTimeRange(sb, start, end);
        sb.Append(' ');

        var services = string.IsNullOrWhiteSpace(serviceName)
            ? Array.Empty<string>()
            : new[] { serviceName.Trim() };
        AppendStreamFilter(sb, projectId, services, environment);

        if (levels.Count > 0)
        {
            var validLevels = levels
                .Select(x => x.Trim().ToUpperInvariant())
                .Where(AllowedLevels.Contains)
                .Distinct()
                .ToList();

            if (validLevels.Count == 1)
            {
                sb.Append(" level:").Append(validLevels[0]);
            }
            else if (validLevels.Count > 1)
            {
                sb.Append(" (");
                sb.Append(string.Join(" OR ", validLevels.Select(l => "level:" + l)));
                sb.Append(')');
            }
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            AppendKeywordFilter(sb, keyword);
        }

        if (!string.IsNullOrWhiteSpace(exceptionType))
        {
            sb.Append(" exception_type:").Append(Quote(exceptionType.Trim()));
        }

        sb.Append(" | stats count()");
        return sb.ToString();
    }

    /// <summary>
    /// 构造按时间桶聚合的趋势查询。
    /// interval 示例：1m、5m、15m、1h。
    /// </summary>
    public static string BuildTrend(
        long projectId,
        DateTimeOffset start,
        DateTimeOffset end,
        string interval,
        bool errorsOnly)
    {
        var sb = new StringBuilder();
        AppendTimeRange(sb, start, end);
        sb.Append(' ');
        AppendStreamFilter(sb, projectId, Array.Empty<string>());

        if (errorsOnly)
        {
            sb.Append(" (level:ERROR OR level:FATAL)");
        }

        // 标准化 interval：仅允许数字+单位
        var clean = NormalizeInterval(interval);
        // VictoriaLogs LogsQL 时间桶语法：stats by (_time:5m) count()
        sb.Append(" | stats by (_time:").Append(clean).Append(") count()");
        return sb.ToString();
    }

    private static string NormalizeInterval(string interval)
    {
        if (string.IsNullOrWhiteSpace(interval))
        {
            return "5m";
        }

        var trimmed = interval.Trim().ToLowerInvariant();
        // 允许：数字 + (s|m|h|d)
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+[smhd]$"))
        {
            return trimmed;
        }

        return "5m";
    }

    /// <summary>
    /// 构造错误样本查询：仅 ERROR/FATAL，按时间降序取前 limit 条，用于异常指纹聚合。
    /// </summary>
    public static string BuildErrorSamples(long projectId, DateTimeOffset start, DateTimeOffset end, int limit)
    {
        var sb = new StringBuilder();
        AppendTimeRange(sb, start, end);
        sb.Append(' ');
        AppendStreamFilter(sb, projectId, Array.Empty<string>());
        sb.Append(" (level:ERROR OR level:FATAL)");
        sb.Append(" | sort by (_time desc) | limit ")
            .Append(Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// 构造实时跟随（tail）查询：仅按项目/服务/级别/关键字过滤，不限定时间窗与排序。
    /// </summary>
    public static string BuildTail(
        long projectId,
        IReadOnlyList<string> services,
        IReadOnlyList<string> levels,
        string? keyword)
    {
        var sb = new StringBuilder();
        AppendStreamFilter(sb, projectId, services);

        var validLevels = levels
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(AllowedLevels.Contains)
            .Distinct()
            .ToList();

        if (validLevels.Count == 1)
        {
            sb.Append(" level:").Append(validLevels[0]);
        }
        else if (validLevels.Count > 1)
        {
            sb.Append(" (");
            sb.Append(string.Join(" OR ", validLevels.Select(l => "level:" + l)));
            sb.Append(')');
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            AppendKeywordFilter(sb, keyword);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 追加关键词过滤器：按空白切词，逐词在消息正文与异常字段上做子串匹配（*"..."*），词之间为 AND。
    /// 子串匹配不走词索引，性能由调用方通过时间窗控制。
    /// </summary>
    private static void AppendKeywordFilter(StringBuilder sb, string keyword)
    {
        var terms = keyword
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length <= MaxKeywordTermLength)
            .Take(MaxKeywordTerms)
            .ToList();

        if (terms.Count == 0)
        {
            return;
        }

        var groups = terms.Select(term =>
            "(" + string.Join(
                " OR ",
                KeywordFields.Select(field => field + ":" + SubstringLiteral(term))) + ")");

        sb.Append(' ').Append(string.Join(" AND ", groups));
    }

    /// <summary>子串过滤器字面量：*"..."*。</summary>
    private static string SubstringLiteral(string value)
        => "*" + Quote(value) + "*";

    private static void AppendTimeRange(StringBuilder sb, DateTimeOffset start, DateTimeOffset end)
    {
        // [start, end) — end 已由游标/请求规范化
        sb.Append("_time:[")
            .Append(FormatTime(start))
            .Append(", ")
            .Append(FormatTime(end))
            .Append(')');
    }

    /// <summary>
    /// 追加流过滤器 {project_id=...,environment=...,service_name=...}。
    /// environment 是流字段（_stream_fields 含 environment），作为流过滤可命中索引，直接参与过滤不做丢弃（否则会放宽匹配范围）。
    /// </summary>
    private static void AppendStreamFilter(StringBuilder sb, long projectId, IReadOnlyList<string> services, string? environment = null)
    {
        sb.Append("{project_id=").Append(Quote(projectId.ToString(CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(environment))
        {
            sb.Append(",environment=").Append(Quote(environment.Trim()));
        }

        var cleaned = services
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => s.Length <= 200 && IsSafeToken(s))
            .Distinct(StringComparer.Ordinal)
            .Take(20)
            .ToList();

        if (cleaned.Count == 1)
        {
            sb.Append(",service_name=").Append(Quote(cleaned[0]));
        }

        sb.Append('}');

        if (cleaned.Count > 1)
        {
            sb.Append(" (");
            sb.Append(string.Join(" OR ", cleaned.Select(s => "service_name:" + Quote(s))));
            sb.Append(')');
        }
    }

    public static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return "\"" + escaped + "\"";
    }

    public static string FormatTime(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static bool IsSafeToken(string value)
    {
        foreach (var ch in value)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '/' or ':' or '@'))
            {
                return false;
            }
        }

        return true;
    }
}
