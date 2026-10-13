using System.Data.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LogPilot.Infrastructure.Sqlite;

/// <summary>
/// 创建 SQLite 连接并统一开启 foreign_keys / busy_timeout。
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly SqliteOptions _options;

    public SqliteConnectionFactory(IOptions<StorageOptions> options)
    {
        _options = options.Value.Sqlite;
    }

    public DbConnection CreateConnection()
    {
        EnsureDirectoryExists(_options.DataSource);
        var connection = new SqliteConnection(BuildConnectionString(_options.DataSource));
        return connection;
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = (SqliteConnection)CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return connection;
    }

    internal static string BuildConnectionString(string dataSource)
        => new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Pooling = true,
            DefaultTimeout = 5
        }.ToString();

    private static void EnsureDirectoryExists(string dataSource)
    {
        var fullPath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
