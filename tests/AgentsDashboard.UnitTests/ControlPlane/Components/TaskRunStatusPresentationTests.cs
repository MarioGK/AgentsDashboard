using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Components;
using MudBlazor;

namespace AgentsDashboard.UnitTests.ControlPlane.Components;

public class TaskRunStatusPresentationTests
{
    [Test]
    public void FromRunState_MapsWorkingStates()
    {
        var queued = TaskRunStatusPresentation.FromRunState(RunState.Queued);
        var running = TaskRunStatusPresentation.FromRunState(RunState.Running);
        var pending = TaskRunStatusPresentation.FromRunState(RunState.PendingApproval);

        queued.Label.Should().Be("Queued");
        queued.Color.Should().Be(Color.Warning);
        queued.IsWorking.Should().BeTrue();

        running.Label.Should().Be("Running");
        running.Color.Should().Be(Color.Info);
        running.IsWorking.Should().BeTrue();

        pending.Label.Should().Be("PendingApproval");
        pending.Color.Should().Be(Color.Secondary);
        pending.IsWorking.Should().BeTrue();
    }

    [Test]
    public void FromTaskAndLatestRun_NoRunForEnabledTask_ReturnsIdle()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = true
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, null);

        visual.Status.Should().Be(TaskRunStatus.Idle);
        visual.Label.Should().Be("Idle");
        visual.IsWorking.Should().BeFalse();
        visual.Tooltip.Should().Be("No runs recorded yet.");
    }

    [Test]
    public void FromTaskAndLatestRun_NoRunForDisabledTask_ReturnsObsolete()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = false
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, null);

        visual.Status.Should().Be(TaskRunStatus.Obsolete);
        visual.Label.Should().Be("Obsolete");
        visual.Tooltip.Should().Contain("Task is disabled");
    }

    [Test]
    public void FromTaskAndLatestRun_UsesLatestRunSummaryInTooltip()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = true
        };
        var run = new RunDocument
        {
            Id = "run-1",
            TaskId = task.Id,
            State = RunState.Succeeded,
            Summary = "Completed successfully",
            CreatedAtUtc = new DateTime(2026, 2, 16, 15, 0, 0, DateTimeKind.Utc)
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, run);

        visual.Status.Should().Be(TaskRunStatus.Succeeded);
        visual.Tooltip.Should().Contain("Latest run:");
        visual.Tooltip.Should().Contain("Summary: Completed successfully");
    }

    [Test]
    public void FromTaskAndLatestRun_DisabledTaskWithTerminalLatestRun_ReturnsObsolete()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = false
        };
        var run = new RunDocument
        {
            Id = "run-1",
            TaskId = task.Id,
            State = RunState.Failed
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, run);

        visual.Status.Should().Be(TaskRunStatus.Obsolete);
        visual.Label.Should().Be("Obsolete");
    }

    [Test]
    public void FromTaskAndLatestRun_DisabledTaskWithActiveLatestRun_KeepsActiveState()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = false
        };
        var run = new RunDocument
        {
            Id = "run-1",
            TaskId = task.Id,
            State = RunState.Running
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, run);

        visual.Status.Should().Be(TaskRunStatus.Running);
        visual.IsWorking.Should().BeTrue();
    }
}
