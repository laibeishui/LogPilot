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

    public static string BuildSearch(AuthorizedLogQuery query)
    {
        var sb = new StringBuilder();
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
            sb.Append(' ').Append(Quote(query.Keyword.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            sb.Append(" trace_id:").Append(Quote(query.TraceId.Trim()));
        }

        // 多取 1 条用于判断 HasMore；排序按时间降序
        var limit = Math.Clamp(query.PageSize, 1, 100) + 1;
        sb.Append(" | sort by (_time desc) | limit ").Append(limit.ToString(CultureInfo.InvariantCulture));
        return sb.ToString();
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
        sb.Append(" | fields service_name | uniq | limit 200");
        return sb.ToString();
    }

    private static void AppendTimeRange(StringBuilder sb, DateTimeOffset start, DateTimeOffset end)
    {
        // [start, end) — end 已由游标/请求规范化
        sb.Append("_time:[")
            .Append(FormatTime(start))
            .Append(", ")
            .Append(FormatTime(end))
            .Append(')');
    }

    private static void AppendStreamFilter(StringBuilder sb, long projectId, IReadOnlyList<string> services)
    {
        sb.Append("{project_id=").Append(Quote(projectId.ToString(CultureInfo.InvariantCulture)));

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
