using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Features.Workspace.Services;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceThreadComposerBarTests
{
    [Test]
    public async Task ComposerSendButtonForwardsSubmitEventAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var submitCalls = 0;
        var value = "send this prompt";
        IReadOnlyList<WorkspaceImageInput> images = Array.Empty<WorkspaceImageInput>();

        var component = context.RenderComponent<WorkspaceThreadComposerBar>(parameters => parameters
            .Add(p => p.ComposerInputId, "composer-input-bar")
            .Add(p => p.ComposerValue, value)
            .Add(p => p.OnComposerValueChanged, (string updated) => value = updated)
            .Add(p => p.ComposerImages, images)
            .Add(p => p.OnComposerImagesChanged, (IReadOnlyList<WorkspaceImageInput> updated) => images = updated)
            .Add(p => p.IsSubmitting, false)
            .Add(p => p.IsComposerBlocked, false)
            .Add(p => p.SubmitRequested, () => submitCalls++));

        component.Find("[data-testid='workspace-composer-send']").Click();
        await Assert.That(submitCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ComposerShowsSubmittingAndBlockedStatesAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

        var component = context.RenderComponent<WorkspaceThreadComposerBar>(parameters => parameters
            .Add(p => p.ComposerInputId, "composer-input-bar")
            .Add(p => p.ComposerValue, string.Empty)
            .Add(p => p.ComposerImages, Array.Empty<WorkspaceImageInput>())
            .Add(p => p.IsSubmitting, true)
            .Add(p => p.IsComposerBlocked, true)
            .Add(p => p.ComposerBlockReason, "Run blocked"));

        await Assert.That(component.Markup.Contains("Sending...", StringComparison.Ordinal)).IsTrue();
        await Assert.That(component.Markup.Contains("Run blocked", StringComparison.Ordinal)).IsTrue();
    }
}
