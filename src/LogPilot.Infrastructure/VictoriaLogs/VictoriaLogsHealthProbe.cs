using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LogPilot.Infrastructure.VictoriaLogs;

/// <summary>
/// 探测 SQLite 可写与 VictoriaLogs 可达性。
/// </summary>
public sealed class VictoriaLogsHealthProbe : IHealthProbe
{
    private readonly ISqliteConnectionFactory _sqlite;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VictoriaLogsOptions _options;
    private readonly ILogger<VictoriaLogsHealthProbe> _logger;

    public VictoriaLogsHealthProbe(
        ISqliteConnectionFactory sqlite,
        IHttpClientFactory httpClientFactory,
        IOptions<StorageOptions> options,
        ILogger<VictoriaLogsHealthProbe> logger)
    {
        _sqlite = sqlite;
        _httpClientFactory = httpClientFactory;
        _options = options.Value.VictoriaLogs;
        _logger = logger;
    }

    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        var sqlite = await CheckSqliteAsync(cancellationToken).ConfigureAwait(false);
        var victoria = await CheckVictoriaLogsAsync(cancellationToken).ConfigureAwait(false);

        return new SystemHealthReport
        {
            CheckedAt = checkedAt,
            LogPilot = new ComponentHealth
            {
                Name = "LogPilot",
                Status = HealthStatus.Healthy,
                Detail = "ready probe"
            },
            Sqlite = sqlite,
            VictoriaLogs = victoria
        };
    }

    private async Task<ComponentHealth> CheckSqliteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _sqlite.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return new ComponentHealth
            {
                Name = "SQLite",
                Status = HealthStatus.Healthy,
                Detail = "reachable"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SQLite readiness check failed");
            return new ComponentHealth
            {
                Name = "SQLite",
                Status = HealthStatus.Unhealthy,
                Detail = "unreachable"
            };
        }
    }

    private async Task<ComponentHealth> CheckVictoriaLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("VictoriaLogs");
            using var response = await client.GetAsync("/", cancellationToken).ConfigureAwait(false);

            // VictoriaLogs 根路径通常返回非 5xx；只要能连通即视为可达。
            var status = response.IsSuccessStatusCode || (int)response.StatusCode < 500
                ? HealthStatus.Healthy
                : HealthStatus.Unhealthy;

            return new ComponentHealth
            {
                Name = "VictoriaLogs",
                Status = status,
                Detail = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VictoriaLogs readiness check failed. BaseUrl={BaseUrl}", _options.BaseUrl);
            return new ComponentHealth
            {
                Name = "VictoriaLogs",
                Status = HealthStatus.Unhealthy,
                Detail = "unreachable"
            };
        }
    }
}
