using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class RunStructuredViewServiceTests
{
    [Test]
    public async Task ApplyStructuredEventAsync_BuildsThinkingToolAndDiffProjection()
    {
        const string runId = "run-projection";
        var timestamp = new DateTime(2026, 2, 17, 12, 10, 0, DateTimeKind.Utc);

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(s => s.ListRunStructuredEventsAsync(runId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = new RunStructuredViewService(store.Object);

        await service.ApplyStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = runId,
                Sequence = 1,
                EventType = "reasoning.delta",
                Category = "reasoning.delta",
                PayloadJson = "{\"thinking\":\"plan the change\"}",
                TimestampUtc = timestamp,
                CreatedAtUtc = timestamp,
            },
            CancellationToken.None);

        await service.ApplyStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = runId,
                Sequence = 2,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolName\":\"bash\",\"toolCallId\":\"call-1\",\"state\":\"started\"}",
                TimestampUtc = timestamp.AddSeconds(1),
                CreatedAtUtc = timestamp.AddSeconds(1),
            },
            CancellationToken.None);

        var toolCompletion = await service.ApplyStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = runId,
                Sequence = 3,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolName\":\"bash\",\"toolCallId\":\"call-1\",\"state\":\"completed\"}",
                TimestampUtc = timestamp.AddSeconds(2),
                CreatedAtUtc = timestamp.AddSeconds(2),
            },
            CancellationToken.None);

        var diffDelta = await service.ApplyStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = runId,
                Sequence = 4,
                EventType = "diff.updated",
                Category = "diff.updated",
                PayloadJson = "{\"diffStat\":\"1 file changed\",\"diffPatch\":\"diff --git a/a.txt b/a.txt\"}",
                TimestampUtc = timestamp.AddSeconds(3),
                CreatedAtUtc = timestamp.AddSeconds(3),
            },
            CancellationToken.None);

        var snapshot = await service.GetViewAsync(runId, CancellationToken.None);

        snapshot.LastSequence.Should().Be(4);
        snapshot.Timeline.Should().HaveCount(4);
        snapshot.Thinking.Should().ContainSingle();
        snapshot.Thinking[0].Content.Should().Be("plan the change");

        snapshot.Tools.Should().ContainSingle();
        snapshot.Tools[0].ToolName.Should().Be("bash");
        snapshot.Tools[0].ToolCallId.Should().Be("call-1");
        snapshot.Tools[0].State.Should().Be("completed");

        snapshot.Diff.Should().NotBeNull();
        snapshot.Diff!.DiffStat.Should().Be("1 file changed");
        snapshot.Diff.DiffPatch.Should().Contain("diff --git a/a.txt b/a.txt");

        toolCompletion.ToolUpdated.Should().NotBeNull();
        toolCompletion.ToolUpdated!.State.Should().Be("completed");
        diffDelta.DiffUpdated.Should().NotBeNull();
        diffDelta.DiffUpdated!.Category.Should().Be("diff.updated");

        store.Verify(s => s.ListRunStructuredEventsAsync(runId, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetViewAsync_HydratesStateUsingSequenceOrder()
    {
        const string runId = "run-hydrate";
        var timestamp = new DateTime(2026, 2, 17, 13, 0, 0, DateTimeKind.Utc);
        var seeded = new List<RunStructuredEventDocument>
        {
            new()
            {
                Id = "event-3",
                RunId = runId,
                Sequence = 3,
                EventType = "diff.updated",
                Category = "diff.updated",
                PayloadJson = "{\"diffStat\":\"2 files changed\",\"diffPatch\":\"diff --git a/x.txt b/x.txt\"}",
                TimestampUtc = timestamp.AddSeconds(3),
                CreatedAtUtc = timestamp.AddSeconds(3),
            },
            new()
            {
                Id = "event-1",
                RunId = runId,
                Sequence = 1,
                EventType = "reasoning.delta",
                Category = "reasoning.delta",
                PayloadJson = "{\"thinking\":\"first\"}",
                TimestampUtc = timestamp.AddSeconds(1),
                CreatedAtUtc = timestamp.AddSeconds(1),
            },
            new()
            {
                Id = "event-2",
                RunId = runId,
                Sequence = 2,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolName\":\"git\",\"toolCallId\":\"call-2\",\"state\":\"started\"}",
                TimestampUtc = timestamp.AddSeconds(2),
                CreatedAtUtc = timestamp.AddSeconds(2),
            }
        };

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(s => s.ListRunStructuredEventsAsync(runId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seeded);

        var service = new RunStructuredViewService(store.Object);

        var snapshot = await service.GetViewAsync(runId, CancellationToken.None);

        snapshot.LastSequence.Should().Be(3);
        snapshot.Timeline.Select(item => item.Sequence).Should().ContainInOrder(1L, 2L, 3L);
        snapshot.Thinking.Should().ContainSingle();
        snapshot.Tools.Should().ContainSingle();
        snapshot.Diff.Should().NotBeNull();
        snapshot.Diff!.DiffStat.Should().Be("2 files changed");
    }

    [Test]
    public async Task ApplyStructuredEventAsync_DoesNotDuplicateEventAlreadySeenDuringHydration()
    {
        const string runId = "run-dedup";
        var timestamp = new DateTime(2026, 2, 17, 13, 15, 0, DateTimeKind.Utc);
        var seeded = new List<RunStructuredEventDocument>
        {
            new()
            {
                Id = "event-1",
                RunId = runId,
                Sequence = 1,
                EventType = "reasoning.delta",
                Category = "reasoning.delta",
                PayloadJson = "{\"thinking\":\"hydrate\"}",
                TimestampUtc = timestamp,
                CreatedAtUtc = timestamp,
            }
        };

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(s => s.ListRunStructuredEventsAsync(runId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seeded);

        var service = new RunStructuredViewService(store.Object);

        var projection = await service.ApplyStructuredEventAsync(
            seeded[0],
            CancellationToken.None);

        projection.Snapshot.Timeline.Should().HaveCount(1);
        projection.Snapshot.Thinking.Should().HaveCount(1);
        projection.Snapshot.LastSequence.Should().Be(1);
    }
}
