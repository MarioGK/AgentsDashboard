using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class GraphWorkflowModelsTests
{
    [Fact]
    public void WorkflowNodeType_Start_Is0()
    {
        ((int)WorkflowNodeType.Start).Should().Be(0);
    }

    [Fact]
    public void WorkflowNodeType_Agent_Is1()
    {
        ((int)WorkflowNodeType.Agent).Should().Be(1);
    }

    [Fact]
    public void WorkflowNodeType_Delay_Is2()
    {
        ((int)WorkflowNodeType.Delay).Should().Be(2);
    }

    [Fact]
    public void WorkflowNodeType_Approval_Is3()
    {
        ((int)WorkflowNodeType.Approval).Should().Be(3);
    }

    [Fact]
    public void WorkflowNodeType_End_Is4()
    {
        ((int)WorkflowNodeType.End).Should().Be(4);
    }

    [Fact]
    public void WorkflowV2ExecutionState_Running_Is0()
    {
        ((int)WorkflowV2ExecutionState.Running).Should().Be(0);
    }

    [Fact]
    public void WorkflowV2ExecutionState_Succeeded_Is1()
    {
        ((int)WorkflowV2ExecutionState.Succeeded).Should().Be(1);
    }

    [Fact]
    public void WorkflowNodeState_Pending_Is0()
    {
        ((int)WorkflowNodeState.Pending).Should().Be(0);
    }

    [Fact]
    public void WorkflowNodeState_DeadLettered_Is6()
    {
        ((int)WorkflowNodeState.DeadLettered).Should().Be(6);
    }

    [Fact]
    public void WorkflowNodeConfig_Defaults()
    {
        var node = new WorkflowNodeConfig();

        node.Id.Should().NotBeNullOrEmpty();
        node.Name.Should().BeEmpty();
        node.Type.Should().Be(WorkflowNodeType.Start);
        node.AgentId.Should().BeNull();
        node.DelaySeconds.Should().BeNull();
        node.ApproverRole.Should().BeNull();
        node.TimeoutMinutes.Should().BeNull();
        node.RetryPolicy.Should().BeNull();
        node.InputMappings.Should().BeEmpty();
        node.OutputMappings.Should().BeEmpty();
        node.PositionX.Should().Be(0);
        node.PositionY.Should().Be(0);
    }

    [Fact]
    public void WorkflowEdgeConfig_Defaults()
    {
        var edge = new WorkflowEdgeConfig();

        edge.Id.Should().NotBeNullOrEmpty();
        edge.SourceNodeId.Should().BeEmpty();
        edge.TargetNodeId.Should().BeEmpty();
        edge.Condition.Should().BeEmpty();
        edge.Priority.Should().Be(0);
        edge.Label.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowV2TriggerConfig_Defaults()
    {
        var trigger = new WorkflowV2TriggerConfig();

        trigger.Type.Should().Be("Manual");
        trigger.CronExpression.Should().BeEmpty();
        trigger.WebhookEventFilter.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowV2Document_Defaults()
    {
        var workflow = new WorkflowV2Document();

        workflow.Id.Should().NotBeNullOrEmpty();
        workflow.RepositoryId.Should().BeEmpty();
        workflow.Name.Should().BeEmpty();
        workflow.Description.Should().BeEmpty();
        workflow.Nodes.Should().BeEmpty();
        workflow.Edges.Should().BeEmpty();
        workflow.Trigger.Should().NotBeNull();
        workflow.Trigger.Type.Should().Be("Manual");
        workflow.WebhookToken.Should().BeEmpty();
        workflow.Enabled.Should().BeTrue();
        workflow.MaxConcurrentNodes.Should().Be(4);
        workflow.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        workflow.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void WorkflowExecutionV2Document_Defaults()
    {
        var execution = new WorkflowExecutionV2Document();

        execution.Id.Should().NotBeNullOrEmpty();
        execution.WorkflowV2Id.Should().BeEmpty();
        execution.RepositoryId.Should().BeEmpty();
        execution.ProjectId.Should().BeEmpty();
        execution.State.Should().Be(WorkflowV2ExecutionState.Running);
        execution.CurrentNodeId.Should().BeEmpty();
        execution.Context.Should().BeEmpty();
        execution.NodeResults.Should().BeEmpty();
        execution.PendingApprovalNodeId.Should().BeEmpty();
        execution.ApprovedBy.Should().BeEmpty();
        execution.FailureReason.Should().BeEmpty();
        execution.TriggeredBy.Should().BeEmpty();
        execution.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        execution.StartedAtUtc.Should().BeNull();
        execution.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public void WorkflowNodeResult_Defaults()
    {
        var result = new WorkflowNodeResult();

        result.NodeId.Should().BeEmpty();
        result.NodeName.Should().BeEmpty();
        result.NodeType.Should().Be(WorkflowNodeType.Start);
        result.State.Should().Be(WorkflowNodeState.Pending);
        result.RunId.Should().BeNull();
        result.Summary.Should().BeEmpty();
        result.Attempt.Should().Be(1);
        result.OutputContext.Should().BeEmpty();
        result.StartedAtUtc.Should().BeNull();
        result.EndedAtUtc.Should().BeNull();
    }

    [Fact]
    public void WorkflowDeadLetterDocument_Defaults()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.Id.Should().NotBeNullOrEmpty();
        dl.ExecutionId.Should().BeEmpty();
        dl.WorkflowV2Id.Should().BeEmpty();
        dl.FailedNodeId.Should().BeEmpty();
        dl.FailedNodeName.Should().BeEmpty();
        dl.FailureReason.Should().BeEmpty();
        dl.InputContextSnapshot.Should().BeEmpty();
        dl.RunId.Should().BeNull();
        dl.Attempt.Should().Be(1);
        dl.Replayed.Should().BeFalse();
        dl.ReplayedExecutionId.Should().BeNull();
        dl.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
        dl.ReplayedAtUtc.Should().BeNull();
    }

    [Fact]
    public void WorkflowNodeResult_Serialization_RoundTrips()
    {
        var original = new WorkflowNodeResult
        {
            NodeId = "node-1",
            NodeName = "TestNode",
            NodeType = WorkflowNodeType.Agent,
            State = WorkflowNodeState.Succeeded,
            RunId = "run-42",
            Summary = "All tests passed",
            Attempt = 2,
            StartedAtUtc = new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 2, 14, 10, 5, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<WorkflowNodeResult>(json);

        deserialized.Should().NotBeNull();
        deserialized!.NodeId.Should().Be("node-1");
        deserialized.NodeName.Should().Be("TestNode");
        deserialized.NodeType.Should().Be(WorkflowNodeType.Agent);
        deserialized.State.Should().Be(WorkflowNodeState.Succeeded);
        deserialized.RunId.Should().Be("run-42");
        deserialized.Summary.Should().Be("All tests passed");
        deserialized.Attempt.Should().Be(2);
        deserialized.StartedAtUtc.Should().Be(new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc));
        deserialized.EndedAtUtc.Should().Be(new DateTime(2026, 2, 14, 10, 5, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void WorkflowV2Document_Serialization_RoundTrips()
    {
        var original = new WorkflowV2Document
        {
            Id = "wf-serialization",
            RepositoryId = "repo-1",
            Name = "Test Workflow",
            Description = "Serialization test",
            Nodes =
            [
                new WorkflowNodeConfig { Id = "s", Name = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNodeConfig { Id = "a", Name = "Agent1", Type = WorkflowNodeType.Agent, AgentId = "agent-1" },
                new WorkflowNodeConfig { Id = "e", Name = "End", Type = WorkflowNodeType.End }
            ],
            Edges =
            [
                new WorkflowEdgeConfig { Id = "e1", SourceNodeId = "s", TargetNodeId = "a" },
                new WorkflowEdgeConfig { Id = "e2", SourceNodeId = "a", TargetNodeId = "e", Condition = "run.state == Succeeded" }
            ],
            Enabled = true,
            MaxConcurrentNodes = 8
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<WorkflowV2Document>(json);

        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("wf-serialization");
        deserialized.RepositoryId.Should().Be("repo-1");
        deserialized.Name.Should().Be("Test Workflow");
        deserialized.Description.Should().Be("Serialization test");
        deserialized.Nodes.Should().HaveCount(3);
        deserialized.Nodes[0].Type.Should().Be(WorkflowNodeType.Start);
        deserialized.Nodes[1].Type.Should().Be(WorkflowNodeType.Agent);
        deserialized.Nodes[1].AgentId.Should().Be("agent-1");
        deserialized.Nodes[2].Type.Should().Be(WorkflowNodeType.End);
        deserialized.Edges.Should().HaveCount(2);
        deserialized.Edges[1].Condition.Should().Be("run.state == Succeeded");
        deserialized.Enabled.Should().BeTrue();
        deserialized.MaxConcurrentNodes.Should().Be(8);
    }
}
