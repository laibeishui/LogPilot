using LogPilot.Core.Abstractions;
using LogPilot.Core.Models;

namespace LogPilot.Application.Ingestion;

public interface ILogIngestionService
{
    Task<LogWriteResult> IngestAsync(
        long projectId,
        IReadOnlyList<NormalizedLog> logs,
        CancellationToken cancellationToken = default);
}

public sealed class LogIngestionService : ILogIngestionService
{
    private readonly ILogWriter _writer;

    public LogIngestionService(ILogWriter writer)
    {
        _writer = writer;
    }

    public Task<LogWriteResult> IngestAsync(
        long projectId,
        IReadOnlyList<NormalizedLog> logs,
        CancellationToken cancellationToken = default)
    {
        // 再次确保所有记录的 projectId 与凭据一致
        if (logs.Any(x => x.ProjectId != projectId))
        {
            throw new Common.AppException("INVALID_ARGUMENT", "日志项目标识与凭据不一致。");
        }

        return _writer.WriteAsync(logs, cancellationToken);
    }
}
