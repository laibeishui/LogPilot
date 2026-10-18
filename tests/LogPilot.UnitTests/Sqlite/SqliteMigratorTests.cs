using Dapper;
using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.Options;
using LogPilot.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LogPilot.UnitTests.Sqlite;

/// <summary>
/// 迁移脚本以嵌入资源随程序集发布，这里验证它们能被完整发现、按序应用且可重复执行。
/// </summary>
public sealed class SqliteMigratorTests : IDisposable
{
    private static readonly string[] ExpectedTables =
    [
        "lp_user",
        "lp_project",
        "lp_project_member",
        "lp_application",
        "lp_api_key",
        "lp_system_setting",
        "lp_alert_channel",
        "lp_alert_rule",
        "lp_alert_event"
    ];

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"logpilot-migrator-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task MigrateAsync_AppliesEmbeddedMigrations_AndIsIdempotent()
    {
        var factory = CreateFactory();
        var migrator = new SqliteMigrator(factory, NullLogger<SqliteMigrator>.Instance);

        await migrator.MigrateAsync();
        var afterFirstRun = await ReadAppliedVersionsAsync(factory);

        // 嵌入式资源应全部被发现并按版本号递增应用
        var embeddedCount = typeof(SqliteMigrator).Assembly.GetManifestResourceNames()
            .Count(name => name.StartsWith("LogPilot.Infrastructure.Sqlite.Migrations.", StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(embeddedCount, afterFirstRun.Count);
        Assert.Equal(afterFirstRun.OrderBy(v => v).ToList(), afterFirstRun);

        foreach (var table in ExpectedTables)
        {
            await using var db = await factory.OpenConnectionAsync();
            var exists = await db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @Name;",
                new { Name = table });
            Assert.Equal(1, exists);
        }

        // 重复执行必须幂等：既不报错，也不重复记录迁移版本
        await migrator.MigrateAsync();
        Assert.Equal(afterFirstRun, await ReadAppliedVersionsAsync(factory));
    }

    [Fact]
    public async Task MigrateAsync_RebuildMigration_ProducesLatestRuleSchema()
    {
        var factory = CreateFactory();
        var migrator = new SqliteMigrator(factory, NullLogger<SqliteMigrator>.Instance);

        await migrator.MigrateAsync();

        await using var db = await factory.OpenConnectionAsync();
        var columns = (await db.QueryAsync<string>("SELECT name FROM pragma_table_info('lp_alert_rule');")).ToList();

        // 0003 通过重建表新增静默期字段，重建失败会直接丢失该列
        Assert.Contains("silence_until", columns);
        Assert.Contains("condition_type", columns);
        Assert.Contains("notify_recovery", columns);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // 临时文件清理失败不影响测试结论
            }
        }
    }

    private SqliteConnectionFactory CreateFactory() => new(Options.Create(new StorageOptions
    {
        Sqlite = new SqliteOptions { DataSource = _dbPath }
    }));

    private static async Task<List<int>> ReadAppliedVersionsAsync(ISqliteConnectionFactory factory)
    {
        await using var db = await factory.OpenConnectionAsync();
        var versions = await db.QueryAsync<int>("SELECT version FROM lp_schema_migration ORDER BY version;");
        return versions.ToList();
    }
}
