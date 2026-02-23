using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;
using Microsoft.AspNetCore.Components;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceAdvancedDrawerTests
{
    [Test]
    public async Task OverlayClickClosesDrawerWhenOpenAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var closeCalls = 0;
        RenderFragment child = builder => builder.AddContent(0, "advanced");

        var component = context.RenderComponent<WorkspaceAdvancedDrawer>(parameters => parameters
            .Add(p => p.IsOpen, true)
            .Add(p => p.CloseRequested, () => closeCalls++)
            .Add(p => p.ChildContent, child));

        component.Find(".workspace-advanced-overlay").Click();
        await Assert.That(closeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task OverlayClickDoesNothingWhenClosedAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var closeCalls = 0;
        RenderFragment child = builder => builder.AddContent(0, "advanced");

        var component = context.RenderComponent<WorkspaceAdvancedDrawer>(parameters => parameters
            .Add(p => p.IsOpen, false)
            .Add(p => p.CloseRequested, () => closeCalls++)
            .Add(p => p.ChildContent, child));

        component.Find(".workspace-advanced-overlay").Click();
        await Assert.That(closeCalls).IsEqualTo(0);
    }
}
