using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class DeadLetterTests
{
    [Fact]
    public void DefaultId_IsGenerated()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.Id.Should().NotBeNullOrEmpty();
        dl.Id.Should().HaveLength(32);
    }

    [Fact]
    public void DefaultReplayed_IsFalse()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.Replayed.Should().BeFalse();
    }

    [Fact]
    public void DefaultAttempt_IsOne()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.Attempt.Should().Be(1);
    }

    [Fact]
    public void DefaultCreatedAtUtc_IsRecentUtc()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Replayed_CanBeSetTrue()
    {
        var dl = new WorkflowDeadLetterDocument { Replayed = true };

        dl.Replayed.Should().BeTrue();
    }

    [Fact]
    public void ReplayedExecutionId_CanBeSet()
    {
        var dl = new WorkflowDeadLetterDocument { ReplayedExecutionId = "exec-replay-1" };

        dl.ReplayedExecutionId.Should().Be("exec-replay-1");
    }

    [Fact]
    public void ReplayedAtUtc_CanBeSet()
    {
        var now = DateTime.UtcNow;
        var dl = new WorkflowDeadLetterDocument { ReplayedAtUtc = now };

        dl.ReplayedAtUtc.Should().Be(now);
    }

    [Fact]
    public void FailureReason_CanBeSet()
    {
        var dl = new WorkflowDeadLetterDocument { FailureReason = "Agent timed out after 30 minutes" };

        dl.FailureReason.Should().Be("Agent timed out after 30 minutes");
    }

    [Fact]
    public void InputContextSnapshot_DefaultEmpty()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.InputContextSnapshot.Should().BeEmpty();
    }

    [Fact]
    public void InputContextSnapshot_CanStoreData()
    {
        var dl = new WorkflowDeadLetterDocument();
        dl.InputContextSnapshot["branch"] = JsonSerializer.SerializeToElement("main");
        dl.InputContextSnapshot["retryCount"] = JsonSerializer.SerializeToElement(3);

        dl.InputContextSnapshot.Should().HaveCount(2);
        dl.InputContextSnapshot["branch"].GetString().Should().Be("main");
        dl.InputContextSnapshot["retryCount"].GetInt32().Should().Be(3);
    }

    [Fact]
    public void FailedNodeName_CanBeSet()
    {
        var dl = new WorkflowDeadLetterDocument { FailedNodeName = "RunTests" };

        dl.FailedNodeName.Should().Be("RunTests");
    }

    [Fact]
    public void ExecutionId_CanBeSet()
    {
        var dl = new WorkflowDeadLetterDocument { ExecutionId = "exec-42" };

        dl.ExecutionId.Should().Be("exec-42");
    }
}
