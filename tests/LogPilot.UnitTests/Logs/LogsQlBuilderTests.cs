using LogPilot.Core.Abstractions;
using LogPilot.Infrastructure.VictoriaLogs;
using Xunit;

namespace LogPilot.UnitTests.Logs;

/// <summary>
/// 告警规则的 environment 过滤必须进入流过滤器，否则“只在 prod 环境告警”的规则会命中全部环境。
/// </summary>
public sealed class LogsQlBuilderTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-18T09:00:00Z");
    private static readonly DateTimeOffset End = DateTimeOffset.Parse("2026-10-18T10:00:00Z");

    [Fact]
    public void BuildCount_IncludesEnvironment_InStreamFilter()
    {
        var logsQl = LogsQlBuilder.BuildCount(
            7, Start, End, null, "prod", Array.Empty<string>(), null, null);

        Assert.Contains("{project_id=\"7\",environment=\"prod\"}", logsQl);
        Assert.EndsWith(" | stats count()", logsQl);
    }

    [Fact]
    public void BuildCount_CombinesEnvironmentAndServiceName()
    {
        var logsQl = LogsQlBuilder.BuildCount(
            7, Start, End, "order-api", "prod", Array.Empty<string>(), null, null);

        Assert.Contains("{project_id=\"7\",environment=\"prod\",service_name=\"order-api\"}", logsQl);
    }

    [Fact]
    public void BuildCount_OmitsEnvironment_WhenNotConfigured()
    {
        var logsQl = LogsQlBuilder.BuildCount(
            7, Start, End, null, null, Array.Empty<string>(), null, null);

        Assert.DoesNotContain("environment=", logsQl);
        Assert.Contains("{project_id=\"7\"}", logsQl);
    }

    [Fact]
    public void BuildCount_EscapesQuotesInEnvironment()
    {
        var logsQl = LogsQlBuilder.BuildCount(
            7, Start, End, null, "prod\" OR level:ERROR", Array.Empty<string>(), null, null);

        // 引号被转义，无法逃逸出流过滤器构造注入
        Assert.Contains("environment=\"prod\\\" OR level:ERROR\"", logsQl);
    }

    [Fact]
    public void BuildSearch_StillOmitsEnvironment()
    {
        var logsQl = LogsQlBuilder.BuildSearch(new AuthorizedLogQuery
        {
            ProjectId = 7,
            StartTime = Start,
            EndTime = End
        });

        Assert.DoesNotContain("environment=", logsQl);
    }
}
