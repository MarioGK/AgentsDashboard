using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkflowDagValidatorTests
{
    private readonly Mock<IOrchestratorStore> _store = new();

    private static WorkflowV2Document CreateWorkflow(
        List<WorkflowNodeConfig>? nodes = null,
        List<WorkflowEdgeConfig>? edges = null)
    {
        return new WorkflowV2Document
        {
            Id = "wf-1",
            RepositoryId = "repo-1",
            Nodes = nodes ?? [],
            Edges = edges ?? []
        };
    }

    private static WorkflowNodeConfig Node(string id, string name, WorkflowNodeType type, string? agentId = null)
    {
        return new WorkflowNodeConfig
        {
            Id = id,
            Name = name,
            Type = type,
            AgentId = agentId
        };
    }

    private static WorkflowEdgeConfig Edge(string id, string source, string target, int priority = 0, string condition = "")
    {
        return new WorkflowEdgeConfig
        {
            Id = id,
            SourceNodeId = source,
            TargetNodeId = target,
            Priority = priority,
            Condition = condition
        };
    }

    [Test]
    public async Task Valid_SimpleLinearWorkflow_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e")]);

        _store.Setup(s => s.GetAgentAsync("agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument { Id = "agent-1" });

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Valid_BranchingWorkflow_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e1", "End1", WorkflowNodeType.End), Node("e2", "End2", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e1", 0), Edge("e3", "a", "e2", 1)]);

        _store.Setup(s => s.GetAgentAsync("agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument { Id = "agent-1" });

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Invalid_NoStartNode_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("e", "End", WorkflowNodeType.End)],
            []);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Start node"));
    }

    [Test]
    public async Task Invalid_MultipleStartNodes_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s1", "Start1", WorkflowNodeType.Start), Node("s2", "Start2", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s1", "e"), Edge("e2", "s2", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Start nodes"));
    }

    [Test]
    public async Task Invalid_NoEndNode_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start)],
            []);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("End node"));
    }

    [Test]
    public async Task Invalid_DuplicateNodeIds_Fails()
    {
        var workflow = CreateWorkflow(
            [
                new WorkflowNodeConfig { Id = "dup", Name = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNodeConfig { Id = "dup", Name = "End", Type = WorkflowNodeType.End }
            ],
            []);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unique"));
    }

    [Test]
    public async Task Invalid_CycleDetection_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "A", WorkflowNodeType.Agent, "agent-1"), Node("b", "B", WorkflowNodeType.Agent, "agent-2"), Node("c", "C", WorkflowNodeType.Agent, "agent-3"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "b"), Edge("e3", "b", "c"), Edge("e4", "c", "a"), Edge("e5", "a", "e", 1)]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cycle"));
    }

    [Test]
    public async Task Invalid_UnreachableNode_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End), Node("x", "Orphan", WorkflowNodeType.Agent, "agent-2")],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e")]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("reachable") || e.Contains("Orphan"));
    }

    [Test]
    public async Task Invalid_StartNodeHasIncomingEdge_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e"), Edge("e3", "a", "s", 1)]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Start") && e.Contains("incoming"));
    }

    [Test]
    public async Task Invalid_EndNodeHasOutgoingEdge_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e"), Edge("e3", "e", "a")]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("End") && e.Contains("outgoing"));
    }

    [Test]
    public async Task Invalid_DuplicateEdgePriorities_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("b", "Agent2", WorkflowNodeType.Agent, "agent-2"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a", 0), Edge("e2", "s", "b", 0), Edge("e3", "a", "e"), Edge("e4", "b", "e")]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("duplicate priorities"));
    }

    [Test]
    public async Task Invalid_EdgeReferencesUnknownSource_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "unknown", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown source"));
    }

    [Test]
    public async Task Invalid_EdgeReferencesUnknownTarget_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "unknown")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("unknown target"));
    }

    [Test]
    public async Task Invalid_AgentNodeWithoutAgentId_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("AgentId"));
    }

    [Test]
    public async Task Invalid_AgentNodeWithNonExistentAgent_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "missing-agent"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e")]);

        _store.Setup(s => s.GetAgentAsync("missing-agent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentDocument?)null);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non-existent"));
    }

    [Test]
    public async Task Valid_AgentNodeWithValidAgent_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e")]);

        _store.Setup(s => s.GetAgentAsync("agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument { Id = "agent-1", Name = "TestAgent" });

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Valid_DelayNode_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), new WorkflowNodeConfig { Id = "d", Name = "Delay", Type = WorkflowNodeType.Delay, DelaySeconds = 30 }, Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "d"), Edge("e2", "d", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Valid_ApprovalNode_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), new WorkflowNodeConfig { Id = "ap", Name = "Approve", Type = WorkflowNodeType.Approval, ApproverRole = "admin" }, Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "ap"), Edge("e2", "ap", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Valid_MultipleEndNodes_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e1", "End1", WorkflowNodeType.End), Node("e2", "End2", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "e1", 0, "run.state == Succeeded"), Edge("e3", "a", "e2", 1, "run.state == Failed")]);

        _store.Setup(s => s.GetAgentAsync("agent-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument { Id = "agent-1" });

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Invalid_SelfLoop_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "Agent1", WorkflowNodeType.Agent, "agent-1"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a"), Edge("e2", "a", "a"), Edge("e3", "a", "e", 1)]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("cycle"));
    }

    [Test]
    public async Task Valid_EmptyConditions_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Valid_ComplexDag_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("a", "A", WorkflowNodeType.Agent, "agent-1"), Node("b", "B", WorkflowNodeType.Agent, "agent-2"), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "a", 0), Edge("e2", "s", "b", 1), Edge("e3", "a", "e"), Edge("e4", "b", "e")]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Invalid_DisconnectedSubgraph_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End), Node("x1", "Detached1", WorkflowNodeType.Agent, "agent-1"), Node("x2", "Detached2", WorkflowNodeType.Agent, "agent-2")],
            [Edge("e1", "s", "e"), Edge("e2", "x1", "x2")]);

        _store.Setup(s => s.GetAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentDocument());

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("reachable") || e.Contains("Detached"));
    }

    [Test]
    public async Task Valid_SingleStartAndEnd_Passes()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start), Node("e", "End", WorkflowNodeType.End)],
            [Edge("e1", "s", "e")]);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task Invalid_OnlyStartNode_Fails()
    {
        var workflow = CreateWorkflow(
            [Node("s", "Start", WorkflowNodeType.Start)],
            []);

        var result = await WorkflowDagValidator.ValidateAsync(workflow, _store.Object, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("End node"));
    }
}
