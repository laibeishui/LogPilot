using Google.Protobuf;
using LogPilot.Infrastructure.Otlp;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using Xunit;

namespace LogPilot.UnitTests.Ingestion;

public sealed class OtlpLogMapperTests
{
    [Fact]
    public void Map_InjectsProjectId_AndReadsServiceName()
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new Resource()
        };
        resourceLogs.Resource.Attributes.Add(new KeyValue
        {
            Key = "service.name",
            Value = new AnyValue { StringValue = "hilink-api" }
        });
        resourceLogs.Resource.Attributes.Add(new KeyValue
        {
            Key = "deployment.environment.name",
            Value = new AnyValue { StringValue = "production" }
        });

        var scope = new ScopeLogs();
        var record = new LogRecord
        {
            TimeUnixNano = 1_697_169_600_000_000_000UL,
            SeverityNumber = SeverityNumber.Error,
            SeverityText = "Error",
            Body = new AnyValue { StringValue = "timeout" },
            TraceId = ByteString.CopyFrom(Convert.FromHexString("4bf92f3577b34da6a3ce929d0e0e4736"))
        };
        record.Attributes.Add(new KeyValue
        {
            Key = "project_id",
            Value = new AnyValue { StringValue = "hack" }
        });
        scope.LogRecords.Add(record);
        resourceLogs.ScopeLogs.Add(scope);
        request.ResourceLogs.Add(resourceLogs);

        var logs = OtlpLogMapper.Map(request, projectId: 7, receivedAt: DateTimeOffset.UtcNow);

        Assert.Single(logs);
        Assert.Equal(7, logs[0].ProjectId);
        Assert.Equal("hilink-api", logs[0].ServiceName);
        Assert.Equal("production", logs[0].Environment);
        Assert.Equal("ERROR", logs[0].Level);
        Assert.Equal("timeout", logs[0].Message);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", logs[0].TraceId);
        Assert.DoesNotContain(logs[0].Attributes, x => x.Key == "project_id");
    }
}
