using System.Reflection;
using System.Text;
using Dapper;
using LogPilot.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LogPilot.Infrastructure.Sqlite;

/// <summary>
/// 启动时按嵌入式 SQL 迁移文件升级 SQLite schema。
/// </summary>
public sealed class SqliteMigrator
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<SqliteMigrator> _logger;

    public SqliteMigrator(
        ISqliteConnectionFactory connectionFactory,
        ILogger<SqliteMigrator> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = (SqliteConnection)await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        // WAL 必须在事务外设置
        await connection.ExecuteAsync("PRAGMA journal_mode = WAL;").ConfigureAwait(false);

        await connection.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS lp_schema_migration (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at TEXT NOT NULL
            );
            """).ConfigureAwait(false);

        var applied = (await connection.QueryAsync<int>(
                "SELECT version FROM lp_schema_migration ORDER BY version")
            .ConfigureAwait(false)).ToHashSet();

        foreach (var migration in LoadEmbeddedMigrations())
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            _logger.LogInformation("Applying SQLite migration {Version}: {Name}", migration.Version, migration.Name);

            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await connection.ExecuteAsync(migration.Sql, transaction: transaction).ConfigureAwait(false);
                await connection.ExecuteAsync(
                    """
                    INSERT INTO lp_schema_migration(version, name, applied_at)
                    VALUES (@Version, @Name, @AppliedAt);
                    """,
                    new
                    {
                        migration.Version,
                        migration.Name,
                        AppliedAt = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                    },
                    transaction).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
    }

    private static IEnumerable<MigrationScript> LoadEmbeddedMigrations()
    {
        var assembly = typeof(SqliteMigrator).Assembly;
        var prefix = "LogPilot.Infrastructure.Sqlite.Migrations.";

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(resourceName =>
            {
                var fileName = resourceName[prefix.Length..];
                var versionText = fileName.Split('_', 2)[0];
                if (!int.TryParse(versionText, out var version))
                {
                    throw new InvalidOperationException($"Invalid migration resource name: {resourceName}");
                }

                using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Missing embedded migration: {resourceName}");
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var sql = reader.ReadToEnd();

                return new MigrationScript(version, Path.GetFileNameWithoutExtension(fileName), sql);
            })
            .OrderBy(x => x.Version);
    }

    private sealed record MigrationScript(int Version, string Name, string Sql);
}
