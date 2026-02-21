using System.Collections.Concurrent;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class BackgroundWorkSchedulerTests
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

            await Assert.That(secondWorkId).IsEqualTo(firstWorkId);

            await WaitForStateAsync(scheduler, firstWorkId, BackgroundWorkState.Running);

            gate.TrySetResult();
            var completed = await WaitForStateAsync(scheduler, firstWorkId, BackgroundWorkState.Succeeded);
            await Assert.That(completed.PercentComplete).IsEqualTo(100);
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

            await Assert.That(states).Contains(BackgroundWorkState.Pending);
            await Assert.That(states).Contains(BackgroundWorkState.Running);
            await Assert.That(states).Contains(BackgroundWorkState.Succeeded);

            var pendingIndex = states.IndexOf(BackgroundWorkState.Pending);
            var firstRunningIndex = states.IndexOf(BackgroundWorkState.Running);
            var succeededIndex = states.LastIndexOf(BackgroundWorkState.Succeeded);
            await Assert.That(pendingIndex).IsGreaterThanOrEqualTo(0);
            await Assert.That(firstRunningIndex).IsGreaterThan(pendingIndex);
            await Assert.That(succeededIndex).IsGreaterThan(firstRunningIndex);
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
            await Assert.That(failed.ErrorCode).IsEqualTo("exception");
            await Assert.That(failed.ErrorMessage).Contains("boom");
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

        await Assert.That(scheduler.TryGet(workId, out var snapshot)).IsTrue();
        await Assert.That(snapshot.State).IsEqualTo(BackgroundWorkState.Cancelled);
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
                BackgroundWorkKind.TaskRuntimeImageResolution,
                "test:relay-succeeded",
                async (ct, progress) =>
                {
                    progress.Report(new BackgroundWorkSnapshot(
                        WorkId: string.Empty,
                        OperationKey: string.Empty,
                        Kind: BackgroundWorkKind.TaskRuntimeImageResolution,
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
                BackgroundWorkKind.TaskRuntimeImageResolution,
                "test:relay-failed",
                (_, _) => throw new InvalidOperationException("cannot pull"),
                dedupeByOperationKey: true);

            await WaitForStateAsync(scheduler, failedWorkId, BackgroundWorkState.Failed);
            await WaitForNotificationCountAsync(sink, 4);

            await Assert.That(sink.Messages.Any(message =>
                message.Title.Contains("queued", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(sink.Messages.Any(message =>
                message.Title.Contains("running", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(sink.Messages.Any(message =>
                message.Title.Contains("succeeded", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(sink.Messages.Any(message =>
                message.Title.Contains("failed", StringComparison.OrdinalIgnoreCase))).IsTrue();
            await Assert.That(sink.Messages).Contains(message => message.Severity == NotificationSeverity.Error);
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

    private sealed partial class InMemoryNotificationSink : INotificationSink
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

    }
}
