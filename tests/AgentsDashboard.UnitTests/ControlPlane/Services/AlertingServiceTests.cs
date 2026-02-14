using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class AlertingServiceTests
{
    private readonly FakeAlertDataProvider _dataProvider;

    public AlertingServiceTests()
    {
        _dataProvider = new FakeAlertDataProvider();
    }

    private static AlertRuleDocument CreateRule(
        AlertRuleType type,
        int threshold = 5,
        int windowMinutes = 10,
        string webhookUrl = "")
    {
        return new AlertRuleDocument
        {
            Id = $"rule-{Guid.NewGuid():N}",
            Name = $"Test {type} Rule",
            RuleType = type,
            Threshold = threshold,
            WindowMinutes = windowMinutes,
            WebhookUrl = webhookUrl,
            Enabled = true
        };
    }

    private static WorkerRegistration CreateWorker(
        string workerId,
        bool online = true,
        DateTime? lastHeartbeat = null)
    {
        return new WorkerRegistration
        {
            WorkerId = workerId,
            Online = online,
            LastHeartbeatUtc = lastHeartbeat ?? DateTime.UtcNow
        };
    }

    private static RunDocument CreateRun(
        RunState state,
        DateTime? endedAt = null,
        string prUrl = "",
        string outputJson = "",
        DateTime? createdAt = null,
        string repoId = "repo-1")
    {
        return new RunDocument
        {
            Id = $"run-{Guid.NewGuid():N}",
            RepositoryId = repoId,
            State = state,
            EndedAtUtc = endedAt,
            PrUrl = prUrl,
            OutputJson = outputJson,
            CreatedAtUtc = createdAt ?? DateTime.UtcNow
        };
    }

    [Fact]
    public async Task CheckMissingHeartbeat_NoStaleWorkers_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: true, DateTime.UtcNow.AddMinutes(-1)),
            CreateWorker("worker-2", online: true, DateTime.UtcNow.AddMinutes(-2))
        };

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
        message.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckMissingHeartbeat_WithStaleWorkers_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: true, DateTime.UtcNow.AddMinutes(-10)),
            CreateWorker("worker-2", online: true, DateTime.UtcNow.AddMinutes(-1))
        };

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("worker(s) missing heartbeat");
        message.Should().Contain("worker-1");
    }

    [Fact]
    public async Task CheckMissingHeartbeat_MultipleStaleWorkers_ReportsAll()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: true, DateTime.UtcNow.AddMinutes(-10)),
            CreateWorker("worker-2", online: true, DateTime.UtcNow.AddMinutes(-15)),
            CreateWorker("worker-3", online: true, DateTime.UtcNow.AddMinutes(-1))
        };

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("worker-1");
        message.Should().Contain("worker-2");
        message.Should().NotContain("worker-3");
    }

    [Fact]
    public async Task CheckMissingHeartbeat_OfflineWorkersNotIncluded()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: false, DateTime.UtcNow.AddMinutes(-100)),
            CreateWorker("worker-2", online: true, DateTime.UtcNow.AddMinutes(-1))
        };

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckMissingHeartbeat_NoWorkers_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>();

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckFailureRateSpike_BelowThreshold_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 5, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        var windowStart = DateTime.UtcNow.AddMinutes(-10);
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(9)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(8)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(7))
        };

        var (triggered, message) = await checker.CheckFailureRateSpikeAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckFailureRateSpike_AtThreshold_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 5, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        var windowStart = DateTime.UtcNow.AddMinutes(-10);
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(9)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(8)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(7)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(6)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(5))
        };

        var (triggered, message) = await checker.CheckFailureRateSpikeAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("5 runs failed");
        message.Should().Contain("last 10 minutes");
    }

    [Fact]
    public async Task CheckFailureRateSpike_AboveThreshold_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 3, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        var windowStart = DateTime.UtcNow.AddMinutes(-10);
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(9)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(8)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(7)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(6)),
            CreateRun(RunState.Failed, endedAt: windowStart.AddMinutes(5))
        };

        var (triggered, message) = await checker.CheckFailureRateSpikeAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("5 runs failed");
    }

    [Fact]
    public async Task CheckFailureRateSpike_OldFailuresExcluded()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 2, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-5)),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-30)),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-60))
        };

        var (triggered, message) = await checker.CheckFailureRateSpikeAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckFailureRateSpike_NoFailures_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.FailedRuns = new List<RunDocument>();

        var (triggered, message) = await checker.CheckFailureRateSpikeAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckQueueBacklog_BelowThreshold_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.ActiveRunsCount = 5;

        var (triggered, message) = await checker.CheckQueueBacklogAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckQueueBacklog_AtThreshold_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.ActiveRunsCount = 10;

        var (triggered, message) = await checker.CheckQueueBacklogAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("10 active runs");
        message.Should().Contain("threshold: 10");
    }

    [Fact]
    public async Task CheckQueueBacklog_AboveThreshold_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.ActiveRunsCount = 25;

        var (triggered, message) = await checker.CheckQueueBacklogAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("25 active runs");
    }

    [Fact]
    public async Task CheckQueueBacklog_ZeroQueue_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 1);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.ActiveRunsCount = 0;

        var (triggered, message) = await checker.CheckQueueBacklogAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRepeatedPrFailures_NoPrFailures_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.RepeatedPrFailures, threshold: 3, windowMinutes: 60);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.FailedRuns = new List<RunDocument>();

        var (triggered, message) = await checker.CheckRepeatedPrFailuresAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRepeatedPrFailures_BelowThresholdPerRepo_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.RepeatedPrFailures, threshold: 3, windowMinutes: 60);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-5), prUrl: "https://github.com/org/repo1/pull/1"),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-10), prUrl: "https://github.com/org/repo1/pull/2")
        };

        var (triggered, message) = await checker.CheckRepeatedPrFailuresAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRepeatedPrFailures_AboveThresholdForOneRepo_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.RepeatedPrFailures, threshold: 3, windowMinutes: 60);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-5), prUrl: "https://github.com/org/repo1/pull/1", repoId: "repo-1"),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-10), prUrl: "https://github.com/org/repo1/pull/2", repoId: "repo-1"),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-15), prUrl: "https://github.com/org/repo1/pull/3", repoId: "repo-1"),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-20), prUrl: "https://github.com/org/repo2/pull/1", repoId: "repo-2")
        };

        var (triggered, message) = await checker.CheckRepeatedPrFailuresAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("Repeated PR failures");
        message.Should().Contain("repo-1");
        message.Should().Contain("3 failures");
    }

    [Fact]
    public async Task CheckRepeatedPrFailures_FailuresWithoutPrIgnored()
    {
        var rule = CreateRule(AlertRuleType.RepeatedPrFailures, threshold: 2, windowMinutes: 60);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-5), prUrl: ""),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-10), prUrl: ""),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-15), prUrl: "https://github.com/org/repo1/pull/1")
        };

        var (triggered, message) = await checker.CheckRepeatedPrFailuresAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRepeatedPrFailures_OldFailuresExcluded()
    {
        var rule = CreateRule(AlertRuleType.RepeatedPrFailures, threshold: 2, windowMinutes: 30);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-5), prUrl: "https://github.com/org/repo1/pull/1"),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-60), prUrl: "https://github.com/org/repo1/pull/2")
        };

        var (triggered, message) = await checker.CheckRepeatedPrFailuresAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRouteLeakDetection_BelowThreshold_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.RouteLeakDetection, threshold: 3, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.RecentRuns = new List<RunDocument>
        {
            CreateRun(RunState.Succeeded, outputJson: "{\"url\":\"https://example.com\"}"),
            CreateRun(RunState.Succeeded, outputJson: "{}")
        };

        var (triggered, message) = await checker.CheckRouteLeakDetectionAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRouteLeakDetection_AtThreshold_ReturnsTriggered()
    {
        var rule = CreateRule(AlertRuleType.RouteLeakDetection, threshold: 3, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.RecentRuns = new List<RunDocument>
        {
            CreateRun(RunState.Succeeded, createdAt: now.AddMinutes(-1), outputJson: "{\"url\":\"https://example.com\"}"),
            CreateRun(RunState.Succeeded, createdAt: now.AddMinutes(-2), outputJson: "{\"url\":\"http://test.com\"}"),
            CreateRun(RunState.Succeeded, createdAt: now.AddMinutes(-3), outputJson: "{\"url\":\"https://another.com\"}")
        };

        var (triggered, message) = await checker.CheckRouteLeakDetectionAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        message.Should().Contain("route leaks detected");
        message.Should().Contain("threshold: 3");
    }

    [Fact]
    public async Task CheckRouteLeakDetection_NoUrlsInOutput_ReturnsNotTriggered()
    {
        var rule = CreateRule(AlertRuleType.RouteLeakDetection, threshold: 2, windowMinutes: 10);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.RecentRuns = new List<RunDocument>
        {
            CreateRun(RunState.Succeeded, outputJson: "{\"result\":\"success\"}"),
            CreateRun(RunState.Succeeded, outputJson: "{\"data\":\"no urls here\"}")
        };

        var (triggered, message) = await checker.CheckRouteLeakDetectionAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRouteLeakDetection_OldRunsExcluded()
    {
        var rule = CreateRule(AlertRuleType.RouteLeakDetection, threshold: 2, windowMinutes: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.RecentRuns = new List<RunDocument>
        {
            CreateRun(RunState.Succeeded, createdAt: now.AddMinutes(-1), outputJson: "{\"url\":\"https://example.com\"}"),
            CreateRun(RunState.Succeeded, createdAt: now.AddMinutes(-30), outputJson: "{\"url\":\"https://old.com\"}")
        };

        var (triggered, message) = await checker.CheckRouteLeakDetectionAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRule_UnknownRuleType_ReturnsNotTriggered()
    {
        var rule = CreateRule((AlertRuleType)999, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        var (triggered, message) = await checker.CheckRuleAsync(rule, CancellationToken.None);

        triggered.Should().BeFalse();
        message.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckRule_MissingHeartbeatType_RoutesToCorrectChecker()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: true, DateTime.UtcNow.AddMinutes(-10))
        };

        var (triggered, message) = await checker.CheckRuleAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        _dataProvider.ListWorkersCalled.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRule_FailureRateSpikeType_RoutesToCorrectChecker()
    {
        var rule = CreateRule(AlertRuleType.FailureRateSpike, threshold: 2);
        var checker = new AlertRuleChecker(_dataProvider);

        var now = DateTime.UtcNow;
        _dataProvider.FailedRuns = new List<RunDocument>
        {
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-1)),
            CreateRun(RunState.Failed, endedAt: now.AddMinutes(-2))
        };

        var (triggered, message) = await checker.CheckRuleAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        _dataProvider.ListFailedRunsCalled.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRule_QueueBacklogType_RoutesToCorrectChecker()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        _dataProvider.ActiveRunsCount = 10;

        var (triggered, message) = await checker.CheckRuleAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
        _dataProvider.CountActiveRunsCalled.Should().BeTrue();
    }

    [Fact]
    public void AlertRule_ThresholdZero_StillWorks()
    {
        var rule = CreateRule(AlertRuleType.QueueBacklog, threshold: 0);

        rule.Threshold.Should().Be(0);
    }

    [Fact]
    public void AlertRule_WindowMinutesDefault_IsTen()
    {
        var rule = new AlertRuleDocument
        {
            RuleType = AlertRuleType.FailureRateSpike,
            Threshold = 5
        };

        rule.WindowMinutes.Should().Be(10);
    }

    [Fact]
    public async Task CheckMissingHeartbeat_EdgeCase_ExactThreshold()
    {
        var rule = CreateRule(AlertRuleType.MissingHeartbeat, threshold: 5);
        var checker = new AlertRuleChecker(_dataProvider);

        var exactlyFiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        _dataProvider.Workers = new List<WorkerRegistration>
        {
            CreateWorker("worker-1", online: true, exactlyFiveMinutesAgo)
        };

        var (triggered, message) = await checker.CheckMissingHeartbeatAsync(rule, CancellationToken.None);

        triggered.Should().BeTrue();
    }
}

public interface IAlertDataProvider
{
    Task<List<WorkerRegistration>> ListWorkersAsync(CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken);
    Task<long> CountActiveRunsAsync(CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken);
}

public sealed class FakeAlertDataProvider : IAlertDataProvider
{
    public List<WorkerRegistration> Workers { get; set; } = [];
    public List<RunDocument> FailedRuns { get; set; } = [];
    public List<RunDocument> RecentRuns { get; set; } = [];
    public long ActiveRunsCount { get; set; }
    public bool ListWorkersCalled { get; private set; }
    public bool ListFailedRunsCalled { get; private set; }
    public bool CountActiveRunsCalled { get; private set; }
    public bool ListRecentRunsCalled { get; private set; }

    public Task<List<WorkerRegistration>> ListWorkersAsync(CancellationToken cancellationToken)
    {
        ListWorkersCalled = true;
        return Task.FromResult(Workers);
    }

    public Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        if (state == RunState.Failed)
        {
            ListFailedRunsCalled = true;
            return Task.FromResult(FailedRuns);
        }
        return Task.FromResult(new List<RunDocument>());
    }

    public Task<long> CountActiveRunsAsync(CancellationToken cancellationToken)
    {
        CountActiveRunsCalled = true;
        return Task.FromResult(ActiveRunsCount);
    }

    public Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken)
    {
        ListRecentRunsCalled = true;
        return Task.FromResult(RecentRuns);
    }
}

public class AlertRuleChecker
{
    private readonly IAlertDataProvider _dataProvider;

    public AlertRuleChecker(IAlertDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public async Task<(bool triggered, string message)> CheckRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        return rule.RuleType switch
        {
            AlertRuleType.MissingHeartbeat => await CheckMissingHeartbeatAsync(rule, cancellationToken),
            AlertRuleType.FailureRateSpike => await CheckFailureRateSpikeAsync(rule, cancellationToken),
            AlertRuleType.QueueBacklog => await CheckQueueBacklogAsync(rule, cancellationToken),
            AlertRuleType.RepeatedPrFailures => await CheckRepeatedPrFailuresAsync(rule, cancellationToken),
            AlertRuleType.RouteLeakDetection => await CheckRouteLeakDetectionAsync(rule, cancellationToken),
            _ => (false, string.Empty)
        };
    }

    public async Task<(bool triggered, string message)> CheckMissingHeartbeatAsync(
        AlertRuleDocument rule,
        CancellationToken cancellationToken)
    {
        var workers = await _dataProvider.ListWorkersAsync(cancellationToken);
        var staleThreshold = DateTime.UtcNow.AddMinutes(-rule.Threshold);

        var staleWorkers = workers
            .Where(w => w.Online && w.LastHeartbeatUtc < staleThreshold)
            .ToList();

        if (staleWorkers.Count > 0)
        {
            var workerIds = string.Join(", ", staleWorkers.Select(w => w.WorkerId));
            return (true, $"{staleWorkers.Count} worker(s) missing heartbeat for {rule.Threshold} minutes: {workerIds}");
        }

        return (false, string.Empty);
    }

    public async Task<(bool triggered, string message)> CheckFailureRateSpikeAsync(
        AlertRuleDocument rule,
        CancellationToken cancellationToken)
    {
        var failedRuns = await _dataProvider.ListRunsByStateAsync(RunState.Failed, cancellationToken);
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);
        var recentFailures = failedRuns.Where(r => r.EndedAtUtc >= windowStart).ToList();

        if (recentFailures.Count >= rule.Threshold)
        {
            return (true, $"{recentFailures.Count} runs failed in the last {rule.WindowMinutes} minutes (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }

    public async Task<(bool triggered, string message)> CheckQueueBacklogAsync(
        AlertRuleDocument rule,
        CancellationToken cancellationToken)
    {
        var queuedCount = await _dataProvider.CountActiveRunsAsync(cancellationToken);

        if (queuedCount >= rule.Threshold)
        {
            return (true, $"{queuedCount} active runs in queue (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }

    public async Task<(bool triggered, string message)> CheckRepeatedPrFailuresAsync(
        AlertRuleDocument rule,
        CancellationToken cancellationToken)
    {
        var failedRuns = await _dataProvider.ListRunsByStateAsync(RunState.Failed, cancellationToken);
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);

        var recentFailuresWithPr = failedRuns
            .Where(r => r.EndedAtUtc >= windowStart && !string.IsNullOrWhiteSpace(r.PrUrl))
            .GroupBy(r => r.RepositoryId)
            .Select(g => new { RepositoryId = g.Key, Count = g.Count() })
            .Where(x => x.Count >= rule.Threshold)
            .ToList();

        if (recentFailuresWithPr.Count > 0)
        {
            var repoSummary = string.Join(", ", recentFailuresWithPr.Select(x => $"{x.RepositoryId}: {x.Count} failures"));
            return (true, $"Repeated PR failures detected in {recentFailuresWithPr.Count} repository(ies): {repoSummary}");
        }

        return (false, string.Empty);
    }

    public async Task<(bool triggered, string message)> CheckRouteLeakDetectionAsync(
        AlertRuleDocument rule,
        CancellationToken cancellationToken)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-rule.WindowMinutes);
        var recentRuns = await _dataProvider.ListRecentRunsAsync(cancellationToken);

        var suspiciousRuns = recentRuns
            .Where(r => r.CreatedAtUtc >= windowStart &&
                       !string.IsNullOrWhiteSpace(r.OutputJson) &&
                       (r.OutputJson.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                        r.OutputJson.Contains("https://", StringComparison.OrdinalIgnoreCase)))
            .Take(rule.Threshold)
            .ToList();

        if (suspiciousRuns.Count >= rule.Threshold)
        {
            return (true, $"{suspiciousRuns.Count} runs with potential route leaks detected (threshold: {rule.Threshold})");
        }

        return (false, string.Empty);
    }
}
