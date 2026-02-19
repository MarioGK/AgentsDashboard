using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Components;
using MudBlazor;

namespace AgentsDashboard.UnitTests.ControlPlane.Components;

public class TaskRunStatusPresentationTests
{
    [Test]
    public async Task FromRunState_MapsWorkingStates()
    {
        var queued = TaskRunStatusPresentation.FromRunState(RunState.Queued);
        var running = TaskRunStatusPresentation.FromRunState(RunState.Running);
        var pending = TaskRunStatusPresentation.FromRunState(RunState.PendingApproval);

        await Assert.That(queued.Label).IsEqualTo("Queued");
        await Assert.That(queued.Color).IsEqualTo(Color.Warning);
        await Assert.That(queued.IsWorking).IsTrue();

        await Assert.That(running.Label).IsEqualTo("Running");
        await Assert.That(running.Color).IsEqualTo(Color.Info);
        await Assert.That(running.IsWorking).IsTrue();

        await Assert.That(pending.Label).IsEqualTo("PendingApproval");
        await Assert.That(pending.Color).IsEqualTo(Color.Secondary);
        await Assert.That(pending.IsWorking).IsTrue();
    }

    [Test]
    public async Task FromTaskAndLatestRun_NoRunForEnabledTask_ReturnsInactive()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = true
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, null);

        await Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Inactive);
        await Assert.That(visual.Label).IsEqualTo("Inactive");
        await Assert.That(visual.IsWorking).IsFalse();
        await Assert.That(visual.Tooltip).IsEqualTo("No runs recorded yet.");
    }

    [Test]
    public async Task FromTaskAndLatestRun_NoRunForDisabledTask_ReturnsObsolete()
    {
        var task = new TaskDocument
        {
            Id = "task-1",
            Name = "Task 1",
            Enabled = false
        };

        var visual = TaskRunStatusPresentation.FromTaskAndLatestRun(task, null);

        await Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Obsolete);
        await Assert.That(visual.Label).IsEqualTo("Obsolete");
        await Assert.That(visual.Tooltip).Contains("Task is disabled");
    }

    [Test]
    public async Task FromTaskAndLatestRun_UsesLatestRunSummaryInTooltip()
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

        await Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Inactive);
        await Assert.That(visual.Tooltip).Contains("Latest run:");
        await Assert.That(visual.Tooltip).Contains("Summary: Completed successfully");
    }

    [Test]
    public async Task FromTaskAndLatestRun_DisabledTaskWithTerminalLatestRun_ReturnsObsolete()
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

        await Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Obsolete);
        await Assert.That(visual.Label).IsEqualTo("Obsolete");
    }

    [Test]
    public async Task FromTaskAndLatestRun_DisabledTaskWithActiveLatestRun_KeepsActiveState()
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

        await Assert.That(visual.Status).IsEqualTo(TaskRunStatus.Running);
        await Assert.That(visual.IsWorking).IsTrue();
    }
}
