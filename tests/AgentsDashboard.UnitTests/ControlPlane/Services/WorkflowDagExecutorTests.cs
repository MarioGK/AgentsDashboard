using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkflowDagExecutorTests
{
    private readonly Mock<IOrchestratorStore> _store = new();
    private readonly Mock<IContainerReaper> _containerReaper = new();
    private readonly Mock<IRunEventPublisher> _publisher = new();

    [Fact]
    public void ExecutionV2Document_DefaultState_IsRunning()
    {
        var execution = new WorkflowExecutionV2Document();

        execution.State.Should().Be(WorkflowV2ExecutionState.Running);
    }

    [Fact]
    public void ExecutionV2Document_DefaultNodeResults_IsEmpty()
    {
        var execution = new WorkflowExecutionV2Document();

        execution.NodeResults.Should().BeEmpty();
    }

    [Fact]
    public void ExecutionV2Document_DefaultContext_IsEmpty()
    {
        var execution = new WorkflowExecutionV2Document();

        execution.Context.Should().BeEmpty();
    }

    [Fact]
    public void NodeResult_DefaultAttempt_IsOne()
    {
        var nodeResult = new WorkflowNodeResult();

        nodeResult.Attempt.Should().Be(1);
    }

    [Fact]
    public void WorkflowV2Document_DefaultNodes_IsEmpty()
    {
        var workflow = new WorkflowV2Document();

        workflow.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowV2Document_DefaultEdges_IsEmpty()
    {
        var workflow = new WorkflowV2Document();

        workflow.Edges.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowV2Document_DefaultTrigger_IsManual()
    {
        var workflow = new WorkflowV2Document();

        workflow.Trigger.Type.Should().Be("Manual");
    }

    [Fact]
    public void WorkflowV2Document_DefaultMaxConcurrentNodes_Is4()
    {
        var workflow = new WorkflowV2Document();

        workflow.MaxConcurrentNodes.Should().Be(4);
    }

    [Fact]
    public void WorkflowV2Document_DefaultEnabled_IsTrue()
    {
        var workflow = new WorkflowV2Document();

        workflow.Enabled.Should().BeTrue();
    }

    [Fact]
    public void DeadLetterDocument_DefaultReplayed_IsFalse()
    {
        var deadLetter = new WorkflowDeadLetterDocument();

        deadLetter.Replayed.Should().BeFalse();
    }

    [Fact]
    public void DeadLetterDocument_DefaultAttempt_IsOne()
    {
        var deadLetter = new WorkflowDeadLetterDocument();

        deadLetter.Attempt.Should().Be(1);
    }

    [Fact]
    public void AgentDocument_DefaultHarness_IsCodex()
    {
        var agent = new AgentDocument();

        agent.Harness.Should().Be("codex");
    }

    [Fact]
    public void AgentDocument_DefaultEnabled_IsTrue()
    {
        var agent = new AgentDocument();

        agent.Enabled.Should().BeTrue();
    }

    [Fact]
    public void AgentDocument_DefaultRetryPolicy_HasOneAttempt()
    {
        var agent = new AgentDocument();

        agent.RetryPolicy.MaxAttempts.Should().Be(1);
    }

    [Fact]
    public async Task CancelExecution_ReturnsResult()
    {
        var expectedExecution = new WorkflowExecutionV2Document
        {
            Id = "exec-1",
            State = WorkflowV2ExecutionState.Cancelled
        };

        _store.Setup(s => s.MarkExecutionV2CompletedAsync("exec-1", WorkflowV2ExecutionState.Cancelled, "Cancelled by user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedExecution);

        var executor = CreateExecutor();
        var result = await executor.CancelExecutionAsync("exec-1", CancellationToken.None);

        result.Should().NotBeNull();
        result!.State.Should().Be(WorkflowV2ExecutionState.Cancelled);
        _store.Verify(s => s.MarkExecutionV2CompletedAsync("exec-1", WorkflowV2ExecutionState.Cancelled, "Cancelled by user", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveNode_Approved_CallsApproveStore()
    {
        var expectedExecution = new WorkflowExecutionV2Document
        {
            Id = "exec-1",
            State = WorkflowV2ExecutionState.Running,
            ApprovedBy = "admin"
        };

        _store.Setup(s => s.ApproveExecutionV2NodeAsync("exec-1", "admin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedExecution);

        var executor = CreateExecutor();
        var result = await executor.ApproveWorkflowNodeAsync("exec-1", "admin", true, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ApprovedBy.Should().Be("admin");
        _store.Verify(s => s.ApproveExecutionV2NodeAsync("exec-1", "admin", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApproveNode_Rejected_CancelsExecution()
    {
        var expectedExecution = new WorkflowExecutionV2Document
        {
            Id = "exec-1",
            State = WorkflowV2ExecutionState.Cancelled
        };

        _store.Setup(s => s.MarkExecutionV2CompletedAsync("exec-1", WorkflowV2ExecutionState.Cancelled, "Approval rejected", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedExecution);

        var executor = CreateExecutor();
        var result = await executor.ApproveWorkflowNodeAsync("exec-1", "admin", false, CancellationToken.None);

        result.Should().NotBeNull();
        result!.State.Should().Be(WorkflowV2ExecutionState.Cancelled);
        _store.Verify(s => s.MarkExecutionV2CompletedAsync("exec-1", WorkflowV2ExecutionState.Cancelled, "Approval rejected", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReplayDeadLetter_CreatesNewExecution()
    {
        var deadLetter = new WorkflowDeadLetterDocument
        {
            Id = "dl-1",
            WorkflowV2Id = "wf-1",
            InputContextSnapshot = []
        };

        var workflow = new WorkflowV2Document
        {
            Id = "wf-1",
            RepositoryId = "repo-1",
            Nodes = [
                new WorkflowNodeConfig { Id = "s", Name = "Start", Type = WorkflowNodeType.Start },
                new WorkflowNodeConfig { Id = "e", Name = "End", Type = WorkflowNodeType.End }
            ],
            Edges = [new WorkflowEdgeConfig { Id = "e1", SourceNodeId = "s", TargetNodeId = "e" }]
        };

        var repository = new RepositoryDocument { Id = "repo-1", ProjectId = "proj-1" };

        var newExecution = new WorkflowExecutionV2Document
        {
            Id = "exec-new",
            WorkflowV2Id = "wf-1",
            State = WorkflowV2ExecutionState.Running
        };

        _store.Setup(s => s.GetWorkflowV2Async("wf-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflow);
        _store.Setup(s => s.GetRepositoryAsync("repo-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(repository);
        _store.Setup(s => s.CreateExecutionV2Async(It.IsAny<WorkflowExecutionV2Document>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newExecution);
        _store.Setup(s => s.MarkDeadLetterReplayedAsync("dl-1", "exec-new", It.IsAny<CancellationToken>()))
            .ReturnsAsync(deadLetter);

        var executor = CreateExecutor();
        var result = await executor.ReplayFromDeadLetterAsync(deadLetter, "operator", CancellationToken.None);

        result.Should().NotBeNull();
        _store.Verify(s => s.MarkDeadLetterReplayedAsync("dl-1", "exec-new", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void WorkflowNodeType_EnumValues_AreCorrect()
    {
        ((int)WorkflowNodeType.Start).Should().Be(0);
        ((int)WorkflowNodeType.Agent).Should().Be(1);
        ((int)WorkflowNodeType.Delay).Should().Be(2);
        ((int)WorkflowNodeType.Approval).Should().Be(3);
        ((int)WorkflowNodeType.End).Should().Be(4);
    }

    [Fact]
    public void WorkflowV2ExecutionState_EnumValues_AreCorrect()
    {
        ((int)WorkflowV2ExecutionState.Running).Should().Be(0);
        ((int)WorkflowV2ExecutionState.Succeeded).Should().Be(1);
        ((int)WorkflowV2ExecutionState.Failed).Should().Be(2);
        ((int)WorkflowV2ExecutionState.Cancelled).Should().Be(3);
        ((int)WorkflowV2ExecutionState.PendingApproval).Should().Be(4);
    }

    [Fact]
    public void WorkflowNodeState_EnumValues_AreCorrect()
    {
        ((int)WorkflowNodeState.Pending).Should().Be(0);
        ((int)WorkflowNodeState.Running).Should().Be(1);
        ((int)WorkflowNodeState.Succeeded).Should().Be(2);
        ((int)WorkflowNodeState.Failed).Should().Be(3);
        ((int)WorkflowNodeState.Skipped).Should().Be(4);
        ((int)WorkflowNodeState.TimedOut).Should().Be(5);
        ((int)WorkflowNodeState.DeadLettered).Should().Be(6);
    }

    [Fact]
    public void WorkflowEdgeConfig_DefaultPriority_IsZero()
    {
        var edge = new WorkflowEdgeConfig();

        edge.Priority.Should().Be(0);
    }

    [Fact]
    public void WorkflowNodeConfig_DefaultMappings_AreEmpty()
    {
        var node = new WorkflowNodeConfig();

        node.InputMappings.Should().BeEmpty();
        node.OutputMappings.Should().BeEmpty();
    }

    [Fact]
    public void WorkflowV2TriggerConfig_DefaultType_IsManual()
    {
        var trigger = new WorkflowV2TriggerConfig();

        trigger.Type.Should().Be("Manual");
    }

    [Fact]
    public void AgentDocument_DefaultTimeouts_Has600Seconds()
    {
        var agent = new AgentDocument();

        agent.Timeouts.ExecutionSeconds.Should().Be(600);
    }

    [Fact]
    public void WorkflowNodeResult_OutputContext_IsEmpty()
    {
        var result = new WorkflowNodeResult();

        result.OutputContext.Should().BeEmpty();
    }

    [Fact]
    public void DeadLetterDocument_InputContextSnapshot_IsEmpty()
    {
        var dl = new WorkflowDeadLetterDocument();

        dl.InputContextSnapshot.Should().BeEmpty();
    }

    private WorkflowDagExecutor CreateExecutor()
    {
        return new WorkflowDagExecutor(
            _store.Object,
            null!,
            _containerReaper.Object,
            _publisher.Object,
            Options.Create(new OrchestratorOptions()),
            NullLogger<WorkflowDagExecutor>.Instance);
    }
}
