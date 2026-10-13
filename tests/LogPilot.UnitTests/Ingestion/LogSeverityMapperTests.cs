using LogPilot.Application.Ingestion;
using Xunit;

namespace LogPilot.UnitTests.Ingestion;

public sealed class LogSeverityMapperTests
{
    [Theory]
    [InlineData("Error", "ERROR")]
    [InlineData("warning", "WARN")]
    [InlineData("INFORMATION", "INFO")]
    [InlineData(null, "INFO")]
    public void Normalize_MapsCommonAliases(string? input, string expected)
        => Assert.Equal(expected, LogSeverityMapper.Normalize(input));

    [Theory]
    [InlineData(1, "TRACE")]
    [InlineData(9, "INFO")]
    [InlineData(17, "ERROR")]
    [InlineData(23, "FATAL")]
    public void FromOtlpSeverityNumber_MapsRanges(int number, string expected)
        => Assert.Equal(expected, LogSeverityMapper.FromOtlpSeverityNumber(number));
}
