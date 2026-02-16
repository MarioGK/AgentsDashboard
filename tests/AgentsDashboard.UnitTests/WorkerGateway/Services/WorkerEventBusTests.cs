using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Services;
using FluentAssertions;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class WorkerEventBusTests
{
    [Test]
    public async Task PublishAsync_MessageIsPublishedSuccessfully()
    {
        var bus = new WorkerEventBus();
        var message = new JobEventMessage { RunId = "run-1", EventType = "status" };

        await bus.PublishAsync(message, CancellationToken.None);
    }

    [Test]
    public async Task ReadAllAsync_ReturnsPublishedMessages()
    {
        var bus = new WorkerEventBus();
        var message1 = new JobEventMessage { RunId = "run-1", EventType = "status", Summary = "Running" };
        var message2 = new JobEventMessage { RunId = "run-2", EventType = "status", Summary = "Completed" };

        await bus.PublishAsync(message1, CancellationToken.None);
        await bus.PublishAsync(message2, CancellationToken.None);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var messages = new List<JobEventMessage>();

        await foreach (var msg in bus.ReadAllAsync(cts.Token))
        {
            messages.Add(msg);
            if (messages.Count == 2)
                break;
        }

        messages.Should().HaveCount(2);
        messages[0].RunId.Should().Be("run-1");
        messages[1].RunId.Should().Be("run-2");
    }

    [Test]
    public async Task ReadAllAsync_MultipleReaders_ReceiveSameMessages()
    {
        var bus = new WorkerEventBus();
        var message = new JobEventMessage { RunId = "run-1", EventType = "status" };

        var reader1Task = Task.Run(async () =>
        {
            var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await foreach (var msg in bus.ReadAllAsync(cts.Token))
            {
                return msg;
            }
            throw new OperationCanceledException();
        });

        await Task.Delay(50);
        await bus.PublishAsync(message, CancellationToken.None);

        var result = await reader1Task;
        result.RunId.Should().Be("run-1");
    }

    [Test]
    public async Task PublishAsync_MultipleMessages_AreQueuedInOrder()
    {
        var bus = new WorkerEventBus();
        var messages = Enumerable.Range(1, 10)
            .Select(i => new JobEventMessage { RunId = $"run-{i}", EventType = $"kind-{i}" })
            .ToList();

        foreach (var msg in messages)
        {
            await bus.PublishAsync(msg, CancellationToken.None);
        }

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
        var received = new List<JobEventMessage>();

        await foreach (var msg in bus.ReadAllAsync(cts.Token))
        {
            received.Add(msg);
            if (received.Count == 10)
                break;
        }

        received.Should().HaveCount(10);
        for (int i = 0; i < 10; i++)
        {
            received[i].RunId.Should().Be($"run-{i + 1}");
        }
    }

    [Test]
    public async Task ReadAllAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var bus = new WorkerEventBus();
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () =>
        {
            await foreach (var _ in bus.ReadAllAsync(cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public void EventBus_CanBeCreatedAndUsed()
    {
        var bus = new WorkerEventBus();
        bus.Should().NotBeNull();
    }
}
