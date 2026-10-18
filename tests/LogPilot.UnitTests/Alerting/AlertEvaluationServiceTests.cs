using LogPilot.Application.Alerting;
using LogPilot.Application.Common;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LogPilot.UnitTests.Alerting;

/// <summary>
/// 日志存储不可用时，告警评估必须跳过该规则并保持事件原状，
/// 不能把“查不到”当成“已恢复”，也不能让健康检查误判评估引擎正常。
/// </summary>
public sealed class AlertEvaluationServiceTests
{
    [Fact]
    public async Task EvaluateRuleAsync_DoesNotTouchEvent_WhenLogStoreUnavailable()
    {
        var rule = CreateRule();
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<AppException>(() => service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new StubChannelRepo(),
            new FailingSearcher(),
            new StubDispatcher(),
            new StubProjectRepo(),
            CancellationToken.None));

        Assert.Equal(LogStoreErrors.UnavailableCode, ex.Code);
        Assert.Empty(eventRepo.Writes);
    }

    [Fact]
    public async Task EvaluateRuleAsync_ResolvesEvent_WhenCountIsZero()
    {
        var rule = CreateRule();
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new StubChannelRepo(),
            new ZeroSearcher(),
            new StubDispatcher(),
            new StubProjectRepo(),
            CancellationToken.None);

        var resolution = Assert.Single(eventRepo.Writes);
        Assert.Equal(AlertEventStatus.Resolved, resolution.Status);
    }

    [Fact]
    public async Task EvaluateRuleAsync_KeepsEventUnresolved_WhenRecoveryNotificationFails()
    {
        // 恢复通知发送失败时绝不能先把事件置为 Resolved，否则通知将永久丢失
        var rule = CreateRule(channelIds: "[1]");
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var dispatcher = new RecordingDispatcher(succeeds: false);
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new SingleChannelRepo(),
            new ZeroSearcher(),
            dispatcher,
            new StubProjectRepo(),
            CancellationToken.None);

        Assert.Equal(1, dispatcher.SendCount);
        Assert.Empty(eventRepo.Writes);
    }

    [Fact]
    public async Task EvaluateRuleAsync_RetriesRecoveryNotification_AfterCooldownElapsed()
    {
        var rule = CreateRule(channelIds: "[1]", cooldownMinutes: 0);
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var dispatcher = new RecordingDispatcher(succeeds: false);
        var service = CreateService();

        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new ZeroSearcher(), dispatcher, new StubProjectRepo(), CancellationToken.None);
        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new ZeroSearcher(), dispatcher, new StubProjectRepo(), CancellationToken.None);

        Assert.Equal(2, dispatcher.SendCount);
        Assert.Empty(eventRepo.Writes);
    }

    [Fact]
    public async Task EvaluateRuleAsync_ResolvesEvent_WhenRecoveryNotificationSucceeds()
    {
        var rule = CreateRule(channelIds: "[1]");
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var dispatcher = new RecordingDispatcher(succeeds: true);
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new SingleChannelRepo(),
            new ZeroSearcher(),
            dispatcher,
            new StubProjectRepo(),
            CancellationToken.None);

        var resolution = Assert.Single(eventRepo.Writes);
        Assert.Equal(AlertEventStatus.Resolved, resolution.Status);
        Assert.NotNull(resolution.LastNotifiedAt);
        Assert.Equal(2, resolution.NotifyCount);
    }

    [Fact]
    public async Task EvaluateRuleAsync_ResolvesWithoutNotifying_WhenNoChannelsConfigured()
    {
        // 无可用渠道时无从发送，应直接置为已恢复，而不是无限期停留在未恢复状态
        var rule = CreateRule();
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new StubChannelRepo(),
            new ZeroSearcher(),
            new StubDispatcher(),
            new StubProjectRepo(),
            CancellationToken.None);

        var resolution = Assert.Single(eventRepo.Writes);
        Assert.Equal(AlertEventStatus.Resolved, resolution.Status);
        Assert.Equal(1, resolution.NotifyCount);
    }

    [Fact]
    public async Task EvaluateRuleAsync_DoesNotRecordNotification_WhenSendFails()
    {
        var rule = CreateRule(channelIds: "[1]");
        // 新事件：先落库为未通知，发送失败后不得记录通知时间与次数
        var eventRepo = new RecordingEventRepo(null);
        var dispatcher = new RecordingDispatcher(succeeds: false);
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new SingleChannelRepo(),
            new FixedCountSearcher(10),
            dispatcher,
            new StubProjectRepo(),
            CancellationToken.None);

        Assert.Equal(1, dispatcher.SendCount);
        Assert.Empty(eventRepo.NotifyUpdates);
        var written = Assert.Single(eventRepo.Writes);
        Assert.Equal(AlertEventStatus.Firing, written.Status);
        Assert.Null(written.LastNotifiedAt);
        Assert.Equal(0, written.NotifyCount);
    }

    [Fact]
    public async Task EvaluateRuleAsync_RecordsNotification_WhenSendSucceeds()
    {
        var rule = CreateRule(channelIds: "[1]");
        var eventRepo = new RecordingEventRepo(null);
        var dispatcher = new RecordingDispatcher(succeeds: true);
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new SingleChannelRepo(),
            new FixedCountSearcher(10),
            dispatcher,
            new StubProjectRepo(),
            CancellationToken.None);

        var update = Assert.Single(eventRepo.NotifyUpdates);
        Assert.Equal(1, update.NotifyCount);
    }

    [Fact]
    public async Task EvaluateRuleAsync_RetriesNotification_WhenNeverNotifiedAndCooldownElapsed()
    {
        // CooldownMinutes = 0 表示冷却已过：上一轮失败后本轮应再次尝试
        var rule = CreateRule(channelIds: "[1]", cooldownMinutes: 0);
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule, notified: false));
        var dispatcher = new RecordingDispatcher(succeeds: false);
        var service = CreateService();

        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new FixedCountSearcher(10), dispatcher, new StubProjectRepo(), CancellationToken.None);
        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new FixedCountSearcher(10), dispatcher, new StubProjectRepo(), CancellationToken.None);

        Assert.Equal(2, dispatcher.SendCount);
        Assert.Empty(eventRepo.NotifyUpdates);
    }

    [Fact]
    public async Task EvaluateRuleAsync_DoesNotRetryWithinCooldown_WhenSendFailed()
    {
        var rule = CreateRule(channelIds: "[1]", cooldownMinutes: 30);
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule, notified: false));
        var dispatcher = new RecordingDispatcher(succeeds: false);
        var service = CreateService();

        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new FixedCountSearcher(10), dispatcher, new StubProjectRepo(), CancellationToken.None);
        await service.EvaluateRuleAsync(rule, eventRepo, new SingleChannelRepo(), new FixedCountSearcher(10), dispatcher, new StubProjectRepo(), CancellationToken.None);

        Assert.Equal(1, dispatcher.SendCount);
    }

    [Fact]
    public async Task EvaluateAllRulesAsync_FailsCycle_WhenLogStoreUnavailable()
    {
        var rule = CreateRule();
        var provider = new StubServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IAlertRuleRepository)] = new StubRuleRepo([rule]),
            [typeof(IAlertEventRepository)] = new RecordingEventRepo(CreateFiringEvent(rule)),
            [typeof(IAlertChannelRepository)] = new StubChannelRepo(),
            [typeof(ILogSearcher)] = new FailingSearcher(),
            [typeof(INotificationDispatcher)] = new StubDispatcher(),
            [typeof(IProjectRepository)] = new StubProjectRepo()
        });

        var service = new AlertEvaluationService(
            provider,
            NullLogger<AlertEvaluationService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAllRulesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EvaluateRuleAsync_DoesNotFire_UntilForMinutesSatisfied()
    {
        // ForMinutes = 30：条件首次命中时不得立即触发，需持续命中达标后才告警
        var rule = CreateRule(channelIds: "[1]", forMinutes: 30);
        var eventRepo = new RecordingEventRepo(null);
        var dispatcher = new RecordingDispatcher(succeeds: true);
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new SingleChannelRepo(),
            new FixedCountSearcher(10),
            dispatcher,
            new StubProjectRepo(),
            CancellationToken.None);

        Assert.Empty(eventRepo.Writes);
        Assert.Equal(0, dispatcher.SendCount);
    }

    [Fact]
    public async Task EvaluateRuleAsync_DoesNotResolve_WhileForMinutesStillArmed()
    {
        // 持续命中未达标期间，已有告警事件必须保持原状，绝不能因“本轮未触发”被误判为恢复
        var rule = CreateRule(forMinutes: 30);
        var eventRepo = new RecordingEventRepo(CreateFiringEvent(rule));
        var service = CreateService();

        await service.EvaluateRuleAsync(
            rule,
            eventRepo,
            new StubChannelRepo(),
            new FixedCountSearcher(10),
            new StubDispatcher(),
            new StubProjectRepo(),
            CancellationToken.None);

        Assert.Empty(eventRepo.Writes);
    }

    private static AlertEvaluationService CreateService()
        => new(new StubServiceProvider(new Dictionary<Type, object>()),
               NullLogger<AlertEvaluationService>.Instance);

    private static AlertRule CreateRule(string channelIds = "[]", int cooldownMinutes = 5, int forMinutes = 0) => new()
    {
        Id = 1,
        ProjectId = 2,
        Name = "错误数超阈值",
        ConditionType = AlertConditionType.Count,
        Threshold = 5,
        WindowMinutes = 5,
        ForMinutes = forMinutes,
        CooldownMinutes = cooldownMinutes,
        ChannelIds = channelIds,
        Severity = AlertSeverity.P2
    };

    private static AlertEvent CreateFiringEvent(AlertRule rule, bool notified = true) => new()
    {
        Id = 9,
        RuleId = rule.Id,
        ProjectId = rule.ProjectId,
        Fingerprint = "fp",
        Status = AlertEventStatus.Firing,
        Severity = rule.Severity,
        Title = "title",
        Message = "message",
        TriggeredAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        LastNotifiedAt = notified ? DateTimeOffset.UtcNow.AddMinutes(-10) : null,
        NotifyCount = notified ? 1 : 0
    };

    private sealed record EventWrite(
        long Id,
        AlertEventStatus Status,
        DateTimeOffset? ResolvedAt,
        DateTimeOffset? LastNotifiedAt,
        int NotifyCount);

    private sealed record NotifyUpdate(long Id, DateTimeOffset LastNotifiedAt, int NotifyCount);

    private sealed class RecordingEventRepo : IAlertEventRepository
    {
        private readonly AlertEvent? _active;

        public RecordingEventRepo(AlertEvent? active) => _active = active;

        public List<EventWrite> Writes { get; } = [];

        public List<NotifyUpdate> NotifyUpdates { get; } = [];

        public Task<AlertEvent?> FindActiveByFingerprintAsync(
            long ruleId,
            string fingerprint,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_active);

        public Task<IReadOnlyList<AlertEvent>> ListByProjectAsync(
            long projectId,
            AlertEventStatus? status,
            int limit,
            long? cursor,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AlertEvent>>(Array.Empty<AlertEvent>());

        public Task<long> InsertOrUpdateAsync(AlertEvent alertEvent, CancellationToken cancellationToken = default)
        {
            Writes.Add(new EventWrite(
                alertEvent.Id, alertEvent.Status, alertEvent.ResolvedAt,
                alertEvent.LastNotifiedAt, alertEvent.NotifyCount));
            return Task.FromResult(alertEvent.Id);
        }

        public Task UpdateStatusAsync(
            long id,
            AlertEventStatus status,
            DateTimeOffset? resolvedAt,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(new EventWrite(id, status, resolvedAt, null, 0));
            return Task.CompletedTask;
        }

        public Task UpdateNotifyInfoAsync(
            long id,
            DateTimeOffset lastNotifiedAt,
            int notifyCount,
            CancellationToken cancellationToken = default)
        {
            NotifyUpdates.Add(new NotifyUpdate(id, lastNotifiedAt, notifyCount));
            return Task.CompletedTask;
        }
    }

    private sealed class StubRuleRepo : IAlertRuleRepository
    {
        private readonly IReadOnlyList<AlertRule> _rules;

        public StubRuleRepo(IReadOnlyList<AlertRule> rules) => _rules = rules;

        public Task<IReadOnlyList<AlertRule>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules);

        public Task<IReadOnlyList<AlertRule>> ListEnabledAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_rules);

        public Task<AlertRule?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

        public Task<long> InsertAsync(AlertRule rule, CancellationToken cancellationToken = default)
            => Task.FromResult(rule.Id);

        public Task UpdateAsync(AlertRule rule, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateSilenceAsync(long id, DateTimeOffset? silenceUntil, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>模拟 VictoriaLogs 不可用：所有查询抛出 LOG_STORE_UNAVAILABLE。</summary>
    private sealed class FailingSearcher : ILogSearcher
    {
        public Task<LogSearchResult> SearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public Task<LogDetail?> GetByEventIdAsync(long projectId, string eventId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public Task<IReadOnlyList<string>> ListServicesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public Task<long> CountAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string? serviceName, string? environment, IReadOnlyList<string> levels, string? keyword, string? exceptionType, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public Task<IReadOnlyList<TrendPoint>> TrendAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string interval, bool errorsOnly, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, int limit, CancellationToken cancellationToken = default)
            => throw LogStoreErrors.Unavailable("测试");

        public async IAsyncEnumerable<LogSearchItem> TailAsync(long projectId, IReadOnlyList<string> services, IReadOnlyList<string> levels, string? keyword, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>模拟存储可用但窗口内没有匹配日志。</summary>
    private sealed class ZeroSearcher : ILogSearcher
    {
        public Task<LogSearchResult> SearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new LogSearchResult { Items = Array.Empty<LogSearchItem>(), Total = 0 });

        public Task<LogDetail?> GetByEventIdAsync(long projectId, string eventId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => Task.FromResult<LogDetail?>(null);

        public Task<IReadOnlyList<string>> ListServicesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<long> CountAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string? serviceName, string? environment, IReadOnlyList<string> levels, string? keyword, string? exceptionType, CancellationToken cancellationToken = default)
            => Task.FromResult(0L);

        public Task<IReadOnlyList<TrendPoint>> TrendAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string interval, bool errorsOnly, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TrendPoint>>(Array.Empty<TrendPoint>());

        public Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LogSearchItem>>(Array.Empty<LogSearchItem>());

        public async IAsyncEnumerable<LogSearchItem> TailAsync(long projectId, IReadOnlyList<string> services, IReadOnlyList<string> levels, string? keyword, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubChannelRepo : IAlertChannelRepository
    {
        public Task<IReadOnlyList<AlertChannel>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AlertChannel>>(Array.Empty<AlertChannel>());

        public Task<AlertChannel?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<AlertChannel?>(null);

        public Task<long> InsertAsync(AlertChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult(channel.Id);

        public Task UpdateAsync(AlertChannel channel, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>只返回一个已启用渠道，用于验证通知发送链路。</summary>
    private sealed class SingleChannelRepo : IAlertChannelRepository
    {
        public Task<IReadOnlyList<AlertChannel>> ListByProjectAsync(long projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AlertChannel>>([CreateChannel()]);

        public Task<AlertChannel?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<AlertChannel?>(CreateChannel());

        public Task<long> InsertAsync(AlertChannel channel, CancellationToken cancellationToken = default)
            => Task.FromResult(channel.Id);

        public Task UpdateAsync(AlertChannel channel, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(long id, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static AlertChannel CreateChannel() => new()
        {
            Id = 1,
            ProjectId = 2,
            Name = "webhook",
            Type = AlertChannelType.Webhook,
            ConfigJson = "{}",
            Enabled = EntityStatus.Enabled
        };
    }

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _succeeds;

        public RecordingDispatcher(bool succeeds) => _succeeds = succeeds;

        public int SendCount { get; private set; }

        public Task<NotifyResult> SendToChannelAsync(
            AlertChannel channel,
            AlertNotification notification,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateResult());

        public Task<IReadOnlyList<NotifyResult>> SendToChannelsAsync(
            IEnumerable<AlertChannel> channels,
            AlertNotification notification,
            CancellationToken cancellationToken = default)
        {
            SendCount++;
            return Task.FromResult<IReadOnlyList<NotifyResult>>([CreateResult()]);
        }

        private NotifyResult CreateResult()
            => _succeeds
                ? new NotifyResult { Succeeded = true }
                : new NotifyResult { Succeeded = false, ErrorMessage = "webhook 不可用" };
    }

    /// <summary>固定返回指定条数的检索器，用于驱动触发条件。</summary>
    private sealed class FixedCountSearcher : ILogSearcher
    {
        private readonly long _count;

        public FixedCountSearcher(long count) => _count = count;

        public Task<LogSearchResult> SearchAsync(AuthorizedLogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new LogSearchResult { Items = Array.Empty<LogSearchItem>(), Total = _count });

        public Task<LogDetail?> GetByEventIdAsync(long projectId, string eventId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => Task.FromResult<LogDetail?>(null);

        public Task<IReadOnlyList<string>> ListServicesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<long> CountAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string? serviceName, string? environment, IReadOnlyList<string> levels, string? keyword, string? exceptionType, CancellationToken cancellationToken = default)
            => Task.FromResult(_count);

        public Task<IReadOnlyList<TrendPoint>> TrendAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, string interval, bool errorsOnly, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TrendPoint>>(Array.Empty<TrendPoint>());

        public Task<IReadOnlyList<LogSearchItem>> ListErrorSamplesAsync(long projectId, DateTimeOffset startTime, DateTimeOffset endTime, int limit, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LogSearchItem>>(Array.Empty<LogSearchItem>());

        public async IAsyncEnumerable<LogSearchItem> TailAsync(long projectId, IReadOnlyList<string> services, IReadOnlyList<string> levels, string? keyword, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class StubDispatcher : INotificationDispatcher
    {
        public Task<NotifyResult> SendToChannelAsync(AlertChannel channel, AlertNotification notification, CancellationToken cancellationToken = default)
            => Task.FromResult(new NotifyResult { Succeeded = true });

        public Task<IReadOnlyList<NotifyResult>> SendToChannelsAsync(IEnumerable<AlertChannel> channels, AlertNotification notification, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NotifyResult>>(Array.Empty<NotifyResult>());
    }

    private sealed class StubProjectRepo : IProjectRepository
    {
        public Task<Project?> FindByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(null);

        public Task<Project?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(null);

        public Task<IReadOnlyList<Project>> ListAccessibleAsync(long userId, bool isSystemAdmin, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>(Array.Empty<Project>());

        public Task<long> InsertAsync(Project project, CancellationToken cancellationToken = default)
            => Task.FromResult(project.Id);

        public Task UpdateAsync(Project project, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddMemberAsync(long projectId, long userId, ProjectMemberRole role, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ProjectMemberRole?> GetMemberRoleAsync(long projectId, long userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ProjectMemberRole?>(null);

        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(long projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectMember>>(Array.Empty<ProjectMember>());

        public Task<IReadOnlyDictionary<long, int>> CountMembersByProjectAsync(IReadOnlyList<long> projectIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<long, int>>(new Dictionary<long, int>());

        public Task UpsertMemberAsync(long projectId, long userId, ProjectMemberRole role, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ProjectMembership>> ListMembershipsByUserAsync(long userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProjectMembership>>(Array.Empty<ProjectMembership>());

        public Task ReplaceMembershipsAsync(long userId, IReadOnlyList<(long ProjectId, ProjectMemberRole Role)> memberships, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>按类型解析服务的最小 Scope 容器，用于驱动一个评估周期。</summary>
    private sealed class StubServiceProvider : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly Dictionary<Type, object> _services;

        public StubServiceProvider(Dictionary<Type, object> services) => _services = services;

        public IServiceProvider ServiceProvider => this;

        public IServiceScope CreateScope() => this;

        public object? GetService(Type serviceType)
            => _services.TryGetValue(serviceType, out var service) ? service : null;

        public void Dispose()
        {
        }
    }
}
