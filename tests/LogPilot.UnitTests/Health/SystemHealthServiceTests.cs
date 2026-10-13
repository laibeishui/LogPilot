using LogPilot.Application.Health;
using LogPilot.Core.Abstractions;
using Xunit;

namespace LogPilot.UnitTests.Health;

public sealed class SystemHealthServiceTests
{
    [Fact]
    public async Task GetLiveAsync_DoesNotDependOnExternalStores()
    {
        var probe = new StubProbe();
        var service = new SystemHealthService(probe);

        var report = await service.GetLiveAsync();

        Assert.Equal(HealthStatus.Healthy, report.LogPilot.Status);
        Assert.False(probe.WasCalled);
    }

    [Fact]
    public async Task GetReadyAsync_DelegatesToProbe()
    {
        var probe = new StubProbe();
        var service = new SystemHealthService(probe);

        var report = await service.GetReadyAsync();

        Assert.True(probe.WasCalled);
        Assert.True(report.IsReady);
    }

    private sealed class StubProbe : IHealthProbe
    {
        public bool WasCalled { get; private set; }

        public Task<SystemHealthReport> CheckAsync(CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new SystemHealthReport
            {
                CheckedAt = DateTimeOffset.UtcNow,
                LogPilot = new ComponentHealth { Name = "LogPilot", Status = HealthStatus.Healthy },
                Sqlite = new ComponentHealth { Name = "SQLite", Status = HealthStatus.Healthy },
                VictoriaLogs = new ComponentHealth { Name = "VictoriaLogs", Status = HealthStatus.Healthy }
            });
        }
    }
}
