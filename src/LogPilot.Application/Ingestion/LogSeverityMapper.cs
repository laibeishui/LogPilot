namespace LogPilot.Application.Ingestion;

/// <summary>
/// 将多种级别表示归一化为 TRACE/DEBUG/INFO/WARN/ERROR/FATAL。
/// </summary>
public static class LogSeverityMapper
{
    public static string Normalize(string? level)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            return "INFO";
        }

        var value = level.Trim();
        if (int.TryParse(value, out var numeric))
        {
            return FromOtlpSeverityNumber(numeric);
        }

        return value.ToUpperInvariant() switch
        {
            "TRACE" or "VERBOSE" or "TRACELOG" => "TRACE",
            "DEBUG" or "DBG" => "DEBUG",
            "INFO" or "INFORMATION" or "INFORMATIONAL" or "NOTICE" => "INFO",
            "WARN" or "WARNING" => "WARN",
            "ERROR" or "ERR" or "FATAL_ERROR" => "ERROR",
            "FATAL" or "CRITICAL" or "CRIT" or "EMERGENCY" or "ALERT" => "FATAL",
            _ => "INFO"
        };
    }

    /// <summary>
    /// OTLP SeverityNumber：1-4 TRACE, 5-8 DEBUG, 9-12 INFO, 13-16 WARN, 17-20 ERROR, 21-24 FATAL.
    /// </summary>
    public static string FromOtlpSeverityNumber(int severityNumber)
    {
        return severityNumber switch
        {
            >= 1 and <= 4 => "TRACE",
            >= 5 and <= 8 => "DEBUG",
            >= 9 and <= 12 => "INFO",
            >= 13 and <= 16 => "WARN",
            >= 17 and <= 20 => "ERROR",
            >= 21 and <= 24 => "FATAL",
            _ => "INFO"
        };
    }
}
