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

        Assert.That(queued.Label).IsEqualTo("Queued");
        Assert.That(queued.Color).IsEqualTo(Color.Warning);
        Assert.That(queued.IsWorking).IsTrue();

        Assert.That(running.Label).IsEqualTo("Running");
        Assert.That(running.Color).IsEqualTo(Color.Info);
        Assert.That(running.IsWorking).IsTrue();

        Assert.That(pending.Label).IsEqualTo("PendingApproval");
        Assert.That(pending.Color).IsEqualTo(Color.Secondary);
        Assert.That(pending.IsWorking).IsTrue();
    }

    [Test]
    public void FromTaskAndLatestRun_NoRunForEnabledTask_ReturnsInactive()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = true
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, null);

        Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Inactive);
        Assert.That(visual.Label).IsEqualTo("Inactive");
        Assert.That(visual.IsWorking).IsFalse();
        Assert.That(visual.Tooltip).IsEqualTo("No runs recorded yet.");
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

        Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Obsolete);
        Assert.That(visual.Label).IsEqualTo("Obsolete");
        Assert.That(visual.Tooltip).Contains("Task is disabled");
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

        Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Inactive);
        Assert.That(visual.Tooltip).Contains("Latest run:");
        Assert.That(visual.Tooltip).Contains("Summary: Completed successfully");
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

        Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Obsolete);
        Assert.That(visual.Label).IsEqualTo("Obsolete");
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

        Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Running);
        Assert.That(visual.IsWorking).IsTrue();
    }
}
