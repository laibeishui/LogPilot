using LogPilot.Application.Ingestion;
using LogPilot.Application.Options;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace LogPilot.UnitTests.Ingestion;

public sealed class JsonLogIngestServiceTests
{
    [Fact]
    public async Task Ingest_IgnoresClientProjectId_AndWritesNormalizedLogs()
    {
        var writer = new CapturingWriter();
        var service = new JsonLogIngestService(
            new LogIngestionService(writer),
            Options.Create(new IngestionLimits()));

        var principal = new IngestPrincipal
        {
            ApiKeyId = 1,
            ProjectId = 42,
            ProjectCode = "demo",
            KeyPrefix = "lp_abc"
        };

        var response = await service.IngestAsync(
            principal,
            new JsonIngestRequest
            {
                Logs =
                [
                    new JsonLogItem
                    {
                        ProjectId = 999,
                        ServiceName = "api",
                        Level = "Error",
                        Message = "boom",
                        Timestamp = "2026-10-13T03:00:00Z",
                        Attributes = new Dictionary<string, object?>
                        {
                            ["project_id"] = "hack",
                            ["module"] = "Orders"
                        }
                    }
                ]
            },
            "req_1");

        Assert.Equal(1, response.Accepted);
        Assert.Equal("accepted", response.Status);
        Assert.Single(writer.Logs);
        Assert.Equal(42, writer.Logs[0].ProjectId);
        Assert.Equal("ERROR", writer.Logs[0].Level);
        Assert.DoesNotContain(writer.Logs[0].Attributes, x => x.Key.Equals("project_id", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Orders", writer.Logs[0].Attributes["module"]?.ToString());
    }

    private sealed class CapturingWriter : ILogWriter
    {
        public List<NormalizedLog> Logs { get; } = new();

        public Task<LogWriteResult> WriteAsync(
            IReadOnlyList<NormalizedLog> logs,
            CancellationToken cancellationToken = default)
        {
            Logs.AddRange(logs);
            return Task.FromResult(LogWriteResult.Success(logs.Count));
        }
    }
}
