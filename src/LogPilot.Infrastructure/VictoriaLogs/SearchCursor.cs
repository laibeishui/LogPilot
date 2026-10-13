using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LogPilot.Infrastructure.VictoriaLogs;

/// <summary>
/// 时间锚点游标：下一页加载严格早于 CursorTime 的日志。
/// </summary>
public sealed class SearchCursor
{
    public required DateTimeOffset CursorTime { get; init; }

    public string? EventId { get; init; }

    public static string Encode(DateTimeOffset cursorTime, string? eventId)
    {
        var json = JsonSerializer.Serialize(new
        {
            t = cursorTime.ToUniversalTime().ToString("O"),
            id = eventId
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryDecode(string? raw, out SearchCursor cursor)
    {
        cursor = null!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement.GetProperty("t").GetString();
            if (!DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            {
                return false;
            }

            string? id = null;
            if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                id = idProp.GetString();
            }

            cursor = new SearchCursor { CursorTime = time.ToUniversalTime(), EventId = id };
            return true;
        }
        catch
        {
            return false;
        }
    }
}
