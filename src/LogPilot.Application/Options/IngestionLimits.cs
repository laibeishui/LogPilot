namespace LogPilot.Application.Options;

/// <summary>
/// JSON/归一化层限额（与配置 Ingestion 段对应）。
/// </summary>
public sealed class IngestionLimits
{
    public const string SectionName = "Ingestion";

    public int MaxBatchRecords { get; set; } = 500;

    public int MaxBodyBytes { get; set; } = 1_048_576;

    public int MaxRecordBytes { get; set; } = 65_536;

    public int MaxAttributes { get; set; } = 64;

    public int MaxAttributeDepth { get; set; } = 8;

    public int RequestTimeoutSeconds { get; set; } = 10;
}
