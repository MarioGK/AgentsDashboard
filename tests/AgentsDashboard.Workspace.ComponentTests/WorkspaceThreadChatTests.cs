using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceThreadChatTests
{
    [Test]
    public async Task HeaderActionsAndPlanModeCallbacksAreInvokedAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        var refreshSummaryCalls = 0;
        var refreshRunsCalls = 0;
        var toggleAdvancedCalls = 0;
        var planModeValue = false;

        RenderFragment messageList = builder => builder.AddContent(0, "message-list");
        RenderFragment contextPanel = builder => builder.AddContent(0, "context-panel");
        RenderFragment composer = builder => builder.AddContent(0, "composer");

        var component = context.Render<WorkspaceThreadChat>(parameters => parameters
            .Add(p => p.TaskName, "Task Header")
            .Add(p => p.Harness, "codex")
            .Add(p => p.TaskStateLabel, "Queued up")
            .Add(p => p.TaskStateColor, Color.Warning)
            .Add(p => p.RunIdLabel, "abcd1234")
            .Add(p => p.RunStateLabel, "Queued")
            .Add(p => p.RunStateColor, Color.Warning)
            .Add(p => p.IsRunActive, true)
            .Add(p => p.IsPlanModeEnabled, false)
            .Add(p => p.CanRefreshSummary, true)
            .Add(p => p.IsAdvancedOpen, false)
            .Add(p => p.OnRefreshSummary, () => refreshSummaryCalls++)
            .Add(p => p.OnRefreshRuns, () => refreshRunsCalls++)
            .Add(p => p.OnToggleAdvanced, () => toggleAdvancedCalls++)
            .Add(p => p.OnPlanModeChanged, (bool enabled) => planModeValue = enabled)
            .Add(p => p.MessageList, messageList)
            .Add(p => p.ContextPanel, contextPanel)
            .Add(p => p.Composer, composer));

        await Assert.That(component.Find("[data-testid='workspace-task-title']").TextContent).IsEqualTo("Task Header");

        component.Find("[data-testid='workspace-refresh-runs']").Click();
        component.Find("[data-testid='workspace-advanced-toggle']").Click();

        var planSwitch = component.FindComponent<MudSwitch<bool>>();
        await planSwitch.Instance.ValueChanged.InvokeAsync(true);

        await Assert.That(refreshRunsCalls).IsEqualTo(1);
        await Assert.That(toggleAdvancedCalls).IsEqualTo(1);
        await Assert.That(refreshSummaryCalls).IsEqualTo(0);
        await Assert.That(planModeValue).IsTrue();
    }
}
