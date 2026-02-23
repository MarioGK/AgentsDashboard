using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.Contracts.Features.Repositories.Models.Domain;
using AgentsDashboard.Contracts.Features.Runs.Models.Domain;
using AgentsDashboard.Contracts.Features.Shared.Models.Domain;

namespace AgentsDashboard.Workspace.IntegrationTests;

public sealed class WorkspaceStatusTextFormatterTests
{
    [Test]
    public async Task ReturnsWaitingWhenNoRunAndTaskEnabledAsync()
    {
        var task = new TaskDocument
        {
            Enabled = true
        };

        var label = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, latestRun: null);
        await Assert.That(label).IsEqualTo("Waiting");
    }

    [Test]
    public async Task ReturnsPausedWhenNoRunAndTaskDisabledAsync()
    {
        var task = new TaskDocument
        {
            Enabled = false
        };

        var label = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, latestRun: null);
        await Assert.That(label).IsEqualTo("Paused");
    }

    [Test]
    public async Task ReturnsStateSpecificLabelWhenRunExistsAsync()
    {
        var task = new TaskDocument
        {
            Enabled = true
        };
        var run = new RunDocument
        {
            State = RunState.Failed
        };

        var label = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, run);
        await Assert.That(label).IsEqualTo("Needs attention");
    }
}
