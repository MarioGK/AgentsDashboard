using System.Collections.Concurrent;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class BackgroundWorkSchedulerTests
{
    [Test]
    public async Task Enqueue_Dedupes_ByOperationKey_WhenWorkIsActive()
    {
        var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);

        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstWorkId = scheduler.Enqueue(
                BackgroundWorkKind.Other,
                "test:dedupe",
                async (ct, _) => await gate.Task.WaitAsync(ct),
                dedupeByOperationKey: true);

            var secondWorkId = scheduler.Enqueue(
                BackgroundWorkKind.Other,
                "test:dedupe",
                async (ct, _) => await gate.Task.WaitAsync(ct),
                dedupeByOperationKey: true);

            secondWorkId.Should().Be(firstWorkId);

            await WaitForStateAsync(scheduler, firstWorkId, BackgroundWorkState.Running);

            gate.TrySetResult();
            var completed = await WaitForStateAsync(scheduler, firstWorkId, BackgroundWorkState.Succeeded);
            completed.PercentComplete.Should().Be(100);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Progress_Transitions_FromPendingToRunningToSucceeded_InOrder()
    {
        var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);

        var states = new List<BackgroundWorkState>();

        void Handler(BackgroundWorkSnapshot snapshot)
        {
            if (!string.Equals(snapshot.OperationKey, "test:transition", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            states.Add(snapshot.State);
        }

        scheduler.Updated += Handler;

        try
        {
            var workId = scheduler.Enqueue(
                BackgroundWorkKind.Other,
                "test:transition",
                async (ct, progress) =>
                {
                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.Other,
                        State: BackgroundWorkState.Running,
                        PercentComplete: 15,
                        Message: "phase 1",
                        StartedAt: null,
                        UpdatedAt: DateTimeOffset.UtcNow,
                        ErrorCode: null,
                        ErrorMessage: null));

                    await Task.Delay(50, ct);

                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.Other,
                        State: BackgroundWorkState.Running,
                        PercentComplete: 80,
                        Message: "phase 2",
                        StartedAt: null,
                        UpdatedAt: DateTimeOffset.UtcNow,
                        ErrorCode: null,
                        ErrorMessage: null));
                },
                dedupeByOperationKey: true);

            await WaitForStateAsync(scheduler, workId, BackgroundWorkState.Succeeded);

            states.Should().Contain(BackgroundWorkState.Pending);
            states.Should().Contain(BackgroundWorkState.Running);
            states.Should().Contain(BackgroundWorkState.Succeeded);

            var pendingIndex = states.IndexOf(BackgroundWorkState.Pending);
            var firstRunningIndex = states.IndexOf(BackgroundWorkState.Running);
            var succeededIndex = states.LastIndexOf(BackgroundWorkState.Succeeded);
            pendingIndex.Should().BeGreaterThanOrEqualTo(0);
            firstRunningIndex.Should().BeGreaterThan(pendingIndex);
            succeededIndex.Should().BeGreaterThan(firstRunningIndex);
        }
        finally
        {
            scheduler.Updated -= Handler;
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Work_Transitions_ToFailed_WhenDelegateThrows()
    {
        var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);

        try
        {
            var workId = scheduler.Enqueue(
                BackgroundWorkKind.Other,
                "test:failed",
                (_, _) => throw new InvalidOperationException("boom"),
                dedupeByOperationKey: true);

            var failed = await WaitForStateAsync(scheduler, workId, BackgroundWorkState.Failed);
            failed.ErrorCode.Should().Be("exception");
            failed.ErrorMessage.Should().Contain("boom");
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Work_Transitions_ToCancelled_WhenSchedulerStops()
    {
        var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);

        var workId = scheduler.Enqueue(
            BackgroundWorkKind.Other,
            "test:cancelled",
            async (ct, _) => await Task.Delay(TimeSpan.FromSeconds(30), ct),
            dedupeByOperationKey: true);

        await WaitForStateAsync(scheduler, workId, BackgroundWorkState.Running);
        await scheduler.StopAsync(CancellationToken.None);

        scheduler.TryGet(workId, out var snapshot).Should().BeTrue();
        snapshot.State.Should().Be(BackgroundWorkState.Cancelled);
    }

    [Test]
    public async Task NotificationRelay_PublishesNotifications_ForQueuedRunningSucceededAndFailed()
    {
        var scheduler = CreateScheduler();
        var sink = new InMemoryNotificationSink();
        var relay = new BackgroundWorkNotificationRelay(
            scheduler,
            sink,
            NullLogger<BackgroundWorkNotificationRelay>.Instance);

        await scheduler.StartAsync(CancellationToken.None);
        await relay.StartAsync(CancellationToken.None);

        try
        {
            var succeededWorkId = scheduler.Enqueue(
                BackgroundWorkKind.WorkerImageResolution,
                "test:relay-succeeded",
                async (ct, progress) =>
                {
                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.WorkerImageResolution,
                        State: BackgroundWorkState.Running,
                        PercentComplete: 50,
                        Message: "midway",
                        StartedAt: null,
                        UpdatedAt: DateTimeOffset.UtcNow,
                        ErrorCode: null,
                        ErrorMessage: null));
                    await Task.Delay(25, ct);
                },
                dedupeByOperationKey: true);

            await WaitForStateAsync(scheduler, succeededWorkId, BackgroundWorkState.Succeeded);

            var failedWorkId = scheduler.Enqueue(
                BackgroundWorkKind.WorkerImageResolution,
                "test:relay-failed",
                (_, _) => throw new InvalidOperationException("cannot pull"),
                dedupeByOperationKey: true);

            await WaitForStateAsync(scheduler, failedWorkId, BackgroundWorkState.Failed);
            await WaitForNotificationCountAsync(sink, 4);

            sink.Messages.Should().Contain(message =>
                message.Title.Contains("queued", StringComparison.OrdinalIgnoreCase));
            sink.Messages.Should().Contain(message =>
                message.Title.Contains("running", StringComparison.OrdinalIgnoreCase));
            sink.Messages.Should().Contain(message =>
                message.Title.Contains("succeeded", StringComparison.OrdinalIgnoreCase));
            sink.Messages.Should().Contain(message =>
                message.Title.Contains("failed", StringComparison.OrdinalIgnoreCase));
            sink.Messages.Should().Contain(message => message.Severity == NotificationSeverity.Error);
        }
        finally
        {
            await relay.StopAsync(CancellationToken.None);
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private static BackgroundWorkScheduler CreateScheduler()
    {
        return new BackgroundWorkScheduler(NullLogger<BackgroundWorkScheduler>.Instance);
    }

    private static async Task<BackgroundWorkSnapshot> WaitForStateAsync(
        IBackgroundWorkCoordinator coordinator,
        string workId,
        params BackgroundWorkState[] targetStates)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (coordinator.TryGet(workId, out var snapshot) &&
                targetStates.Contains(snapshot.State))
            {
                return snapshot;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for work {workId} to reach state {string.Join(", ", targetStates)}.");
    }

    private static async Task WaitForNotificationCountAsync(InMemoryNotificationSink sink, int minimumCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (sink.Messages.Count >= minimumCount)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for at least {minimumCount} notifications.");
    }

    private sealed class InMemoryNotificationSink : INotificationSink
    {
        private readonly ConcurrentQueue<NotificationEnvelope> _messages = [];

        public IReadOnlyCollection<NotificationEnvelope> Messages => _messages.ToArray();

        public Task PublishAsync(
            string title,
            string? message,
            NotificationSeverity severity,
            NotificationSource source = NotificationSource.BackgroundWork,
            string? correlationId = null)
        {
            _messages.Enqueue(new NotificationEnvelope(title, message, severity, source, correlationId));
            return Task.CompletedTask;
        }

        public sealed record NotificationEnvelope(
            string Title,
            string? Message,
            NotificationSeverity Severity,
            NotificationSource Source,
            string? CorrelationId);
    }
}
