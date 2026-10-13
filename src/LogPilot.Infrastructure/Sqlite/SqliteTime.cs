namespace LogPilot.Infrastructure.Sqlite;

internal static class SqliteTime
{
    public static string ToDb(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    public static DateTimeOffset FromDb(string value)
        => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? FromDbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : FromDb(value);
}
