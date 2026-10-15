using LogPilot.Application.Alerting;
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
    /// <summary>连续失败达到该批次数后，将 VictoriaLogs 标记为 Degraded。</summary>
    private const int DegradedFailureThreshold = 3;

    private readonly ISqliteConnectionFactory _sqlite;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly VictoriaLogsOptions _options;
    private readonly AlertEvaluationService _alertEvaluator;
    private readonly ILogWriteMetrics _writeMetrics;
    private readonly ILogger<VictoriaLogsHealthProbe> _logger;

    public VictoriaLogsHealthProbe(
        ISqliteConnectionFactory sqlite,
        IHttpClientFactory httpClientFactory,
        IOptions<StorageOptions> options,
        AlertEvaluationService alertEvaluator,
        ILogWriteMetrics writeMetrics,
        ILogger<VictoriaLogsHealthProbe> logger)
    {
        _sqlite = sqlite;
        _httpClientFactory = httpClientFactory;
        _options = options.Value.VictoriaLogs;
        _alertEvaluator = alertEvaluator;
        _writeMetrics = writeMetrics;
        _logger = logger;
    }

    public async Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        var sqlite = await CheckSqliteAsync(cancellationToken).ConfigureAwait(false);
        var victoria = await CheckVictoriaLogsAsync(cancellationToken).ConfigureAwait(false);
        var alertEvaluator = CheckAlertEvaluator();

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
            VictoriaLogs = victoria,
            AlertEvaluator = alertEvaluator
        };
    }

    private ComponentHealth CheckAlertEvaluator()
    {
        var lastSuccess = _alertEvaluator.LastSuccessAt;
        var lastRun = _alertEvaluator.LastRunAt;

        if (lastRun is null)
        {
            return new ComponentHealth
            {
                Name = "AlertEvaluator",
                Status = HealthStatus.Degraded,
                Detail = "not started yet"
            };
        }

        if (lastSuccess is null)
        {
            return new ComponentHealth
            {
                Name = "AlertEvaluator",
                Status = HealthStatus.Unhealthy,
                Detail = _alertEvaluator.LastError ?? "no successful run"
            };
        }

        var elapsed = DateTimeOffset.UtcNow - lastSuccess.Value;
        if (elapsed > TimeSpan.FromMinutes(2))
        {
            return new ComponentHealth
            {
                Name = "AlertEvaluator",
                Status = HealthStatus.Unhealthy,
                Detail = $"last success {elapsed.TotalMinutes:F0}min ago; {_alertEvaluator.LastError}"
            };
        }

        return new ComponentHealth
        {
            Name = "AlertEvaluator",
            Status = HealthStatus.Healthy,
            Detail = $"runs={_alertEvaluator.RunCount}, last success {elapsed.TotalSeconds:F0}s ago"
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
        var metrics = _writeMetrics.GetSnapshot();

        try
        {
            var client = _httpClientFactory.CreateClient("VictoriaLogs");
            using var response = await client.GetAsync("/", cancellationToken).ConfigureAwait(false);

            // VictoriaLogs 根路径通常返回非 5xx；只要能连通即视为可达。
            var connected = response.IsSuccessStatusCode || (int)response.StatusCode < 500
                ? HealthStatus.Healthy
                : HealthStatus.Unhealthy;

            return new ComponentHealth
            {
                Name = "VictoriaLogs",
                Status = ApplyWriteFailures(connected, metrics),
                Detail = BuildWriteDetail($"HTTP {(int)response.StatusCode}", metrics)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VictoriaLogs readiness check failed. BaseUrl={BaseUrl}", _options.BaseUrl);
            return new ComponentHealth
            {
                Name = "VictoriaLogs",
                Status = HealthStatus.Unhealthy,
                Detail = BuildWriteDetail("unreachable", metrics)
            };
        }
    }

    /// <summary>
    /// 根路径可达只说明端口活着；写入失败（磁盘满、429、insert 5xx、超时）不会体现为连通性故障。
    /// 连续失败达到阈值时降级，任一次写入成功后自动恢复。
    /// </summary>
    private static HealthStatus ApplyWriteFailures(HealthStatus connected, LogWriteMetricsSnapshot metrics)
        => connected == HealthStatus.Healthy && metrics.ConsecutiveFailures >= DegradedFailureThreshold
            ? HealthStatus.Degraded
            : connected;

    private static string BuildWriteDetail(string connectivity, LogWriteMetricsSnapshot metrics)
    {
        if (metrics.TotalRequests == 0)
        {
            return connectivity;
        }

        var detail =
            $"{connectivity}; writes ok={metrics.TotalRequests - metrics.FailedRequests}/{metrics.TotalRequests}";

        if (metrics.FailedRequests > 0 && metrics.LastFailureAt is { } lastFailureAt)
        {
            var ago = DateTimeOffset.UtcNow - lastFailureAt;
            detail +=
                $", records lost={metrics.FailedRecords}, last failure {FormatAgo(ago)} ago: {metrics.LastFailureReason}";
        }

        return detail;
    }

    private static string FormatAgo(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1
            ? $"{elapsed.TotalMinutes:F0}min"
            : $"{elapsed.TotalSeconds:F0}s";
}
