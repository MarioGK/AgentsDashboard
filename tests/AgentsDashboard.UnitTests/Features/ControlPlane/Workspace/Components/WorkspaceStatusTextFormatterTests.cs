
using AgentsDashboard.ControlPlane.Components.Workspace;

namespace AgentsDashboard.UnitTests.ControlPlane.Components;

public sealed class WorkspaceStatusTextFormatterTests
{
    [Test]
    public async Task FormatTaskStateLabel_NoRunEnabled_ReturnsWaiting()
    {
        var task = new TaskDocument
        {
            Enabled = true,
        };

        var label = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, null);

        await Assert.That(label).IsEqualTo("Waiting");
    }

    [Test]
    public async Task FormatTaskStateLabel_NoRunDisabled_ReturnsPaused()
    {
        var task = new TaskDocument
        {
            Enabled = false,
        };

        var label = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, null);

        await Assert.That(label).IsEqualTo("Paused");
    }

    [Test]
    public async Task FormatTaskStateLabel_RunStates_ReturnsConversationalLabels()
    {
        var task = new TaskDocument
        {
            Enabled = true,
        };

        var running = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.Running });
        var queued = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.Queued });
        var pending = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.PendingApproval });
        var succeeded = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.Succeeded });
        var failed = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.Failed });
        var cancelled = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, new RunDocument { State = RunState.Cancelled });

        await Assert.That(running).IsEqualTo("Running now");
        await Assert.That(queued).IsEqualTo("Queued up");
        await Assert.That(pending).IsEqualTo("Needs input");
        await Assert.That(succeeded).IsEqualTo("Done");
        await Assert.That(failed).IsEqualTo("Needs attention");
        await Assert.That(cancelled).IsEqualTo("Stopped");
    }
}
