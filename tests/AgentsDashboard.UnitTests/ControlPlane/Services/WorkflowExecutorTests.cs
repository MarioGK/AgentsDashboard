using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkflowExecutorTests
{
    [Test]
    public void WorkflowDocument_CreatedWithCorrectProperties()
    {
        var workflow = new WorkflowDocument
        {
            Id = "workflow-1",
            RepositoryId = "repo-1",
            Name = "Test Workflow",
            Description = "Test Description",
            Enabled = true
        };

        workflow.Id.Should().Be("workflow-1");
        workflow.RepositoryId.Should().Be("repo-1");
        workflow.Name.Should().Be("Test Workflow");
        workflow.Description.Should().Be("Test Description");
        workflow.Enabled.Should().BeTrue();
        workflow.Stages.Should().BeEmpty();
    }

    [Test]
    public void WorkflowStageConfig_TaskStage_CreatedCorrectly()
    {
        var stage = new WorkflowStageConfig
        {
            Id = "stage-1",
            Name = "Task Stage",
            Type = WorkflowStageType.Task,
            TaskId = "task-1",
            Order = 0
        };

        stage.Type.Should().Be(WorkflowStageType.Task);
        stage.TaskId.Should().Be("task-1");
        stage.DelaySeconds.Should().BeNull();
        stage.ParallelStageIds.Should().BeNull();
    }

    [Test]
    public void WorkflowStageConfig_ApprovalStage_CreatedCorrectly()
    {
        var stage = new WorkflowStageConfig
        {
            Id = "stage-1",
            Name = "Approval Stage",
            Type = WorkflowStageType.Approval,
            ApproverRole = "admin",
            Order = 0
        };

        stage.Type.Should().Be(WorkflowStageType.Approval);
        stage.ApproverRole.Should().Be("admin");
    }

    [Test]
    public void WorkflowStageConfig_DelayStage_CreatedCorrectly()
    {
        var stage = new WorkflowStageConfig
        {
            Id = "stage-1",
            Name = "Delay Stage",
            Type = WorkflowStageType.Delay,
            DelaySeconds = 60,
            Order = 0
        };

        stage.Type.Should().Be(WorkflowStageType.Delay);
        stage.DelaySeconds.Should().Be(60);
    }

    [Test]
    public void WorkflowStageConfig_ParallelStage_CreatedCorrectly()
    {
        var stage = new WorkflowStageConfig
        {
            Id = "stage-1",
            Name = "Parallel Stage",
            Type = WorkflowStageType.Parallel,
            ParallelStageIds = ["task-1", "task-2", "task-3"],
            Order = 0
        };

        stage.Type.Should().Be(WorkflowStageType.Parallel);
        stage.ParallelStageIds.Should().HaveCount(3);
        stage.ParallelStageIds.Should().Contain("task-1");
        stage.ParallelStageIds.Should().Contain("task-2");
        stage.ParallelStageIds.Should().Contain("task-3");
    }

    [Test]
    public void WorkflowExecutionDocument_CreatedWithCorrectProperties()
    {
        var execution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            WorkflowId = "workflow-1",
            RepositoryId = "repo-1",
            ProjectId = "project-1",
            State = WorkflowExecutionState.Running,
            CurrentStageIndex = 0
        };

        execution.Id.Should().Be("exec-1");
        execution.WorkflowId.Should().Be("workflow-1");
        execution.RepositoryId.Should().Be("repo-1");
        execution.ProjectId.Should().Be("project-1");
        execution.State.Should().Be(WorkflowExecutionState.Running);
        execution.CurrentStageIndex.Should().Be(0);
        execution.StageResults.Should().BeEmpty();
        execution.PendingApprovalStageId.Should().BeEmpty();
        execution.ApprovedBy.Should().BeEmpty();
        execution.FailureReason.Should().BeEmpty();
    }

    [Test]
    public void WorkflowExecutionState_EnumValues()
    {
        ((int)WorkflowExecutionState.Running).Should().Be(0);
        ((int)WorkflowExecutionState.Succeeded).Should().Be(1);
        ((int)WorkflowExecutionState.Failed).Should().Be(2);
        ((int)WorkflowExecutionState.Cancelled).Should().Be(3);
        ((int)WorkflowExecutionState.PendingApproval).Should().Be(4);
    }

    [Test]
    public void WorkflowStageType_EnumValues()
    {
        ((int)WorkflowStageType.Task).Should().Be(0);
        ((int)WorkflowStageType.Approval).Should().Be(1);
        ((int)WorkflowStageType.Delay).Should().Be(2);
        ((int)WorkflowStageType.Parallel).Should().Be(3);
    }

    [Test]
    public void WorkflowStageResult_CreatedCorrectly()
    {
        var result = new WorkflowStageResult
        {
            StageId = "stage-1",
            StageName = "Test Stage",
            StageType = WorkflowStageType.Task,
            Succeeded = true,
            Summary = "Completed successfully",
            RunIds = ["run-1", "run-2"]
        };

        result.StageId.Should().Be("stage-1");
        result.StageName.Should().Be("Test Stage");
        result.StageType.Should().Be(WorkflowStageType.Task);
        result.Succeeded.Should().BeTrue();
        result.Summary.Should().Be("Completed successfully");
        result.RunIds.Should().HaveCount(2);
    }

    [Test]
    public void WorkflowStageResult_DefaultValues()
    {
        var result = new WorkflowStageResult();

        result.Succeeded.Should().BeFalse();
        result.Summary.Should().BeEmpty();
        result.RunIds.Should().BeEmpty();
        result.StageId.Should().BeEmpty();
        result.StageName.Should().BeEmpty();
    }

    [Test]
    public void Workflow_StagesOrderedCorrectly()
    {
        var workflow = new WorkflowDocument
        {
            Id = "workflow-1",
            Stages =
            [
                new WorkflowStageConfig { Id = "stage-3", Name = "Third", Order = 2 },
                new WorkflowStageConfig { Id = "stage-1", Name = "First", Order = 0 },
                new WorkflowStageConfig { Id = "stage-2", Name = "Second", Order = 1 }
            ]
        };

        var orderedStages = workflow.Stages.OrderBy(s => s.Order).ToList();

        orderedStages[0].Name.Should().Be("First");
        orderedStages[1].Name.Should().Be("Second");
        orderedStages[2].Name.Should().Be("Third");
    }

    [Test]
    [Arguments(WorkflowStageType.Task, "Task")]
    [Arguments(WorkflowStageType.Approval, "Approval")]
    [Arguments(WorkflowStageType.Delay, "Delay")]
    [Arguments(WorkflowStageType.Parallel, "Parallel")]
    public void WorkflowStageType_Names(WorkflowStageType type, string expectedName)
    {
        type.ToString().Should().Be(expectedName);
    }

    [Test]
    [Arguments(WorkflowExecutionState.Running, "Running")]
    [Arguments(WorkflowExecutionState.Succeeded, "Succeeded")]
    [Arguments(WorkflowExecutionState.Failed, "Failed")]
    [Arguments(WorkflowExecutionState.Cancelled, "Cancelled")]
    [Arguments(WorkflowExecutionState.PendingApproval, "PendingApproval")]
    public void WorkflowExecutionState_Names(WorkflowExecutionState state, string expectedName)
    {
        state.ToString().Should().Be(expectedName);
    }

    [Test]
    public void WorkflowExecution_MultipleStageResults()
    {
        var execution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            StageResults =
            [
                new WorkflowStageResult { StageId = "stage-1", Succeeded = true },
                new WorkflowStageResult { StageId = "stage-2", Succeeded = true },
                new WorkflowStageResult { StageId = "stage-3", Succeeded = false, Summary = "Failed" }
            ]
        };

        execution.StageResults.Should().HaveCount(3);
        execution.StageResults.Count(r => r.Succeeded).Should().Be(2);
        execution.StageResults.Count(r => !r.Succeeded).Should().Be(1);
    }

    [Test]
    public void WorkflowExecution_PendingApprovalState()
    {
        var execution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            State = WorkflowExecutionState.PendingApproval,
            PendingApprovalStageId = "stage-2"
        };

        execution.State.Should().Be(WorkflowExecutionState.PendingApproval);
        execution.PendingApprovalStageId.Should().Be("stage-2");
    }

    [Test]
    public void WorkflowExecution_AfterApproval()
    {
        var execution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            State = WorkflowExecutionState.PendingApproval,
            PendingApprovalStageId = "stage-2"
        };

        execution.State = WorkflowExecutionState.Running;
        execution.PendingApprovalStageId = string.Empty;
        execution.ApprovedBy = "admin@example.com";

        execution.State.Should().Be(WorkflowExecutionState.Running);
        execution.PendingApprovalStageId.Should().BeEmpty();
        execution.ApprovedBy.Should().Be("admin@example.com");
    }

    [Test]
    public void Workflow_AllStageTypesSupported()
    {
        var workflow = new WorkflowDocument
        {
            Stages =
            [
                new WorkflowStageConfig { Type = WorkflowStageType.Task, TaskId = "task-1", Order = 0 },
                new WorkflowStageConfig { Type = WorkflowStageType.Delay, DelaySeconds = 10, Order = 1 },
                new WorkflowStageConfig { Type = WorkflowStageType.Approval, ApproverRole = "admin", Order = 2 },
                new WorkflowStageConfig { Type = WorkflowStageType.Parallel, ParallelStageIds = ["t1", "t2"], Order = 3 }
            ]
        };

        workflow.Stages.Should().HaveCount(4);
        workflow.Stages.Select(s => s.Type).Distinct().Should().HaveCount(4);
    }

    [Test]
    public void WorkflowStageConfig_DefaultValues()
    {
        var stage = new WorkflowStageConfig();

        stage.Id.Should().NotBeEmpty();
        stage.Name.Should().BeEmpty();
        stage.Type.Should().Be(WorkflowStageType.Task);
        stage.TaskId.Should().BeNull();
        stage.DelaySeconds.Should().BeNull();
        stage.ParallelStageIds.Should().BeNull();
        stage.ApproverRole.Should().BeNull();
        stage.Order.Should().Be(0);
    }

    [Test]
    public void WorkflowDocument_DefaultValues()
    {
        var workflow = new WorkflowDocument();

        workflow.Id.Should().NotBeEmpty();
        workflow.RepositoryId.Should().BeEmpty();
        workflow.Name.Should().BeEmpty();
        workflow.Description.Should().BeEmpty();
        workflow.Stages.Should().BeEmpty();
        workflow.Enabled.Should().BeTrue();
        workflow.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Test]
    public void WorkflowExecutionDocument_DefaultValues()
    {
        var execution = new WorkflowExecutionDocument();

        execution.Id.Should().NotBeEmpty();
        execution.WorkflowId.Should().BeEmpty();
        execution.RepositoryId.Should().BeEmpty();
        execution.ProjectId.Should().BeEmpty();
        execution.State.Should().Be(WorkflowExecutionState.Running);
        execution.CurrentStageIndex.Should().Be(0);
        execution.StageResults.Should().BeEmpty();
        execution.PendingApprovalStageId.Should().BeEmpty();
        execution.ApprovedBy.Should().BeEmpty();
        execution.FailureReason.Should().BeEmpty();
        execution.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Test]
    public void WorkflowStageResult_CanTrackMultipleRunIds()
    {
        var result = new WorkflowStageResult
        {
            StageId = "stage-1",
            RunIds = ["run-1", "run-2", "run-3"]
        };

        result.RunIds.Should().HaveCount(3);
        result.RunIds.Should().Contain("run-1");
        result.RunIds.Should().Contain("run-2");
        result.RunIds.Should().Contain("run-3");
    }

    [Test]
    public void WorkflowStageResult_CanAddRunIds()
    {
        var result = new WorkflowStageResult
        {
            StageId = "stage-1"
        };

        result.RunIds.Add("run-1");
        result.RunIds.Add("run-2");

        result.RunIds.Should().HaveCount(2);
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(1, 2)]
    [Arguments(2, 3)]
    public void WorkflowStageResult_StartedAndEndedAt_AreSet(int delayMs, int expectedOrder)
    {
        var result = new WorkflowStageResult
        {
            StageId = $"stage-{expectedOrder}",
            StartedAtUtc = DateTime.UtcNow
        };

        Thread.Sleep(delayMs);
        result.EndedAtUtc = DateTime.UtcNow;

        result.EndedAtUtc.Should().BeOnOrAfter(result.StartedAtUtc);
    }

    [Test]
    public void WorkflowDocument_CanBeDisabled()
    {
        var workflow = new WorkflowDocument
        {
            Enabled = false
        };

        workflow.Enabled.Should().BeFalse();
    }

    [Test]
    public void WorkflowExecutionDocument_CanTrackProgress()
    {
        var execution = new WorkflowExecutionDocument
        {
            CurrentStageIndex = 2,
            StageResults =
            [
                new WorkflowStageResult { StageId = "stage-1", Succeeded = true },
                new WorkflowStageResult { StageId = "stage-2", Succeeded = true }
            ]
        };

        execution.CurrentStageIndex.Should().Be(2);
        execution.StageResults.Should().HaveCount(2);
    }

    [Test]
    public void WorkflowStageConfig_GeneratesUniqueId()
    {
        var stage1 = new WorkflowStageConfig();
        var stage2 = new WorkflowStageConfig();

        stage1.Id.Should().NotBe(stage2.Id);
        stage1.Id.Should().NotBeEmpty();
        stage2.Id.Should().NotBeEmpty();
    }

    [Test]
    public void WorkflowExecutionDocument_GeneratesUniqueId()
    {
        var exec1 = new WorkflowExecutionDocument();
        var exec2 = new WorkflowExecutionDocument();

        exec1.Id.Should().NotBe(exec2.Id);
    }

    [Test]
    public void WorkflowDocument_GeneratesUniqueId()
    {
        var workflow1 = new WorkflowDocument();
        var workflow2 = new WorkflowDocument();

        workflow1.Id.Should().NotBe(workflow2.Id);
    }

    [Test]
    public void WorkflowExecutionDocument_HasCorrectDefaultState()
    {
        var execution = new WorkflowExecutionDocument();

        execution.State.Should().Be(WorkflowExecutionState.Running);
        execution.CurrentStageIndex.Should().Be(0);
        execution.StageResults.Should().BeEmpty();
    }

    [Test]
    public void WorkflowStageResult_HasCorrectDefaults()
    {
        var result = new WorkflowStageResult();

        result.Succeeded.Should().BeFalse();
        result.Summary.Should().BeEmpty();
        result.RunIds.Should().BeEmpty();
    }

    [Test]
    public void WorkflowDocument_WithMultipleStages_TracksCorrectly()
    {
        var workflow = new WorkflowDocument
        {
            Stages =
            [
                new WorkflowStageConfig { Id = "stage-1", Type = WorkflowStageType.Task, Order = 0 },
                new WorkflowStageConfig { Id = "stage-2", Type = WorkflowStageType.Delay, Order = 1 },
                new WorkflowStageConfig { Id = "stage-3", Type = WorkflowStageType.Approval, Order = 2 }
            ]
        };

        workflow.Stages.Should().HaveCount(3);
        workflow.Stages[0].Type.Should().Be(WorkflowStageType.Task);
        workflow.Stages[1].Type.Should().Be(WorkflowStageType.Delay);
        workflow.Stages[2].Type.Should().Be(WorkflowStageType.Approval);
    }

    [Test]
    public void WorkflowExecutionDocument_StageResults_CanBeEnumerated()
    {
        var execution = new WorkflowExecutionDocument
        {
            StageResults =
            [
                new WorkflowStageResult { StageId = "s1", Succeeded = true },
                new WorkflowStageResult { StageId = "s2", Succeeded = true },
                new WorkflowStageResult { StageId = "s3", Succeeded = false }
            ]
        };

        var succeededCount = execution.StageResults.Count(r => r.Succeeded);
        var failedCount = execution.StageResults.Count(r => !r.Succeeded);

        succeededCount.Should().Be(2);
        failedCount.Should().Be(1);
    }

    [Test]
    [Arguments(WorkflowStageType.Task)]
    [Arguments(WorkflowStageType.Approval)]
    [Arguments(WorkflowStageType.Delay)]
    [Arguments(WorkflowStageType.Parallel)]
    public void WorkflowStageConfig_AllTypesSupported(WorkflowStageType type)
    {
        var stage = new WorkflowStageConfig
        {
            Type = type
        };

        stage.Type.Should().Be(type);
    }
}
