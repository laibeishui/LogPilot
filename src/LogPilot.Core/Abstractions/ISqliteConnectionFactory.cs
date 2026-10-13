using System.Data.Common;

namespace LogPilot.Core.Abstractions;

/// <summary>
/// SQLite 连接工厂。连接打开后应已启用 foreign_keys。
/// </summary>
public interface ISqliteConnectionFactory
{
    DbConnection CreateConnection();

    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
