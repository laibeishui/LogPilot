namespace LogPilot.Infrastructure.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public SqliteOptions Sqlite { get; set; } = new();

    public VictoriaLogsOptions VictoriaLogs { get; set; } = new();
}

public sealed class SqliteOptions
{
    /// <summary>
    /// SQLite 文件路径，例如 /app/data/logpilot.db 或本地 ./data/logpilot.db。
    /// </summary>
    public string DataSource { get; set; } = "./data/logpilot.db";
}

public sealed class VictoriaLogsOptions
{
    public string BaseUrl { get; set; } = "http://localhost:9428";

    public int TimeoutSeconds { get; set; } = 10;
}
