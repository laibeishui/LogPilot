using System.Runtime.CompilerServices;
using LogPilot.Application.Common;
using LogPilot.Application.Logs;
using LogPilot.Application.Options;
using LogPilot.Application.Projects;
using LogPilot.Core.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LogPilot.UnitTests.Logs;

/// <summary>
/// 实时日志（SSE tail）长连接期间的权限复核。
/// </summary>
public sealed class LogQueryServiceTailTests
{
    [Fact]
    public async Task Tail_RevokesAccessMidStream_TerminatesAndStopsDelivering()
    {
        var access = new FakeAccessService { DenyAfterCalls = 1 };
        var service = new LogQueryService(
            new FakeSearcher(itemCount: 2, itemDelay: TimeSpan.FromMilliseconds(1200)),
            access,
            Options.Create(new QueryLimits { StreamRecheckSeconds = 1 }));

        var received = new List<LogSearchItemDto>();

        var ex = await Assert.ThrowsAsync<AppException>(async () =>
        {
            await foreach (var item in service.TailAsync(1, false, 10, null, null, null))
            {
                received.Add(item);
            }
        });

        Assert.Equal("PROJECT_ACCESS_DENIED", ex.Code);
        Assert.Equal(403, ex.StatusCode);

        // 撤权前已推送的一条可以保留，撤权后不得再产出任何日志
        Assert.Single(received);
        Assert.Equal(2, access.ViewChecks); // 建连 1 次 + 间隔到达后复核 1 次
    }

    [Fact]
    public async Task Tail_WithinRecheckInterval_ChecksAccessOnlyOnce()
    {
        var access = new FakeAccessService();
        var service = new LogQueryService(
            new FakeSearcher(itemCount: 3, itemDelay: TimeSpan.FromMilliseconds(10)),
            access,
            Options.Create(new QueryLimits { StreamRecheckSeconds = 30 }));

        var received = new List<LogSearchItemDto>();
        await foreach (var item in service.TailAsync(1, false, 10, null, null, null))
        {
            received.Add(item);
        }

        Assert.Equal(3, received.Count);
        Assert.Equal(1, access.ViewChecks);
    }

    [Fact]
    public async Task Tail_RequiresProjectId()
    {
        var service = new LogQueryService(
            new FakeSearcher(itemCount: 0, itemDelay: TimeSpan.Zero),
            new FakeAccessService(),
            Options.Create(new QueryLimits()));

        var ex = await Assert.ThrowsAsync<AppException>(async () =>
        {
            await foreach (var _ in service.TailAsync(1, false, 0, null, null, null))
            {
            }
        });

        Assert.Equal("INVALID_ARGUMENT", ex.Code);
    }

    private sealed class FakeAccessService : IProjectAccessService
    {
        public int ViewChecks { get; private set; }

        public int DenyAfterCalls { get; init; } = int.MaxValue;

        public Task EnsureCanViewAsync(
            long userId,
            bool isSystemAdmin,
            long projectId,
            CancellationToken cancellationToken = default)
        {
            ViewChecks++;
            if (ViewChecks > DenyAfterCalls)
            {
                throw new AppException("PROJECT_ACCESS_DENIED", "无权访问该项目。", 403);
            }

            return Task.CompletedTask;
        }

        public Task EnsureCanManageAsync(
            long userId,
            bool isSystemAdmin,
            long projectId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EnsureCanManageKeysAsync(
            long userId,
            bool isSystemAdmin,
            long projectId,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSearcher : ILogSearcher
    {
        private readonly int _itemCount;
        private readonly TimeSpan _itemDelay;

        public FakeSearcher(int itemCount, TimeSpan itemDelay)
        {
            _itemCount = itemCount;
            _itemDelay = itemDelay;
        }

        public async IAsyncEnumerable<LogSearchItem> TailAsync(
            long projectId,
            IReadOnlyList<string> services,
            IReadOnlyList<string> levels,
            string? keyword,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < _itemCount; i++)
            {
                if (i > 0 && _itemDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_itemDelay, cancellationToken).ConfigureAwait(false);
                }

                yield return new LogSearchItem
                {
                    Id = $"item-{i}",
                    Timestamp = DateTimeOffset.UtcNow,
                    ServiceName = "svc",
                    Level = "INFO",
                    Message = $"message-{i}"
                };
            }
        }

        public Task<LogSearchResult> SearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<LogDetail?> GetByEventIdAsync(long projectId, string eventId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListServicesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<long> CountAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string? serviceName, string? environment, IReadOnlyList<string> levels, string? keyword, string? exceptionType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TrendPoint>> TrendAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string interval, bool errorsOnly, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, int limit, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
