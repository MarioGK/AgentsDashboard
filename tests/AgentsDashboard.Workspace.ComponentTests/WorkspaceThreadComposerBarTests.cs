using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.Contracts.Features.Workspace.Models.Domain;
using AgentsDashboard.ControlPlane.Features.Workspace.Services;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceThreadComposerBarTests
{
    [Test]
    public async Task ComposerSendButtonForwardsSubmitEventAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        var submitCalls = 0;
        var value = "send this prompt";
        IReadOnlyList<WorkspaceImageInput> images = Array.Empty<WorkspaceImageInput>();

        var component = context.Render<WorkspaceThreadComposerBar>(parameters => parameters
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
        await using var context = WorkspaceBunitTestContext.Create();

        var component = context.Render<WorkspaceThreadComposerBar>(parameters => parameters
            .Add(p => p.ComposerInputId, "composer-input-bar")
            .Add(p => p.ComposerValue, string.Empty)
            .Add(p => p.ComposerImages, Array.Empty<WorkspaceImageInput>())
            .Add(p => p.IsSubmitting, true)
            .Add(p => p.IsComposerBlocked, true)
            .Add(p => p.ComposerBlockReason, "Run blocked"));

        await Assert.That(component.Markup.Contains("Sending...", StringComparison.Ordinal)).IsTrue();
        await Assert.That(component.Markup.Contains("Run blocked", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ComposerRendersQueuedMessagesAndInvokesQueueActionsAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        var drainCalls = 0;
        var clearCalls = 0;
        var queuedMessages = new[]
        {
            new WorkspaceQueuedMessageDocument
            {
                Id = "queue-1",
                TaskId = "task-1",
                RepositoryId = "repo-1",
                Content = "Queued prompt"
            }
        };

        var component = context.Render<WorkspaceThreadComposerBar>(parameters => parameters
            .Add(p => p.ComposerInputId, "composer-input-bar")
            .Add(p => p.ComposerValue, string.Empty)
            .Add(p => p.ComposerImages, Array.Empty<WorkspaceImageInput>())
            .Add(p => p.QueuedMessages, queuedMessages)
            .Add(p => p.IsSubmitting, false)
            .Add(p => p.IsComposerBlocked, false)
            .Add(p => p.DrainQueuedRequested, () => drainCalls++)
            .Add(p => p.ClearQueuedRequested, () => clearCalls++));

        await Assert.That(component.Markup.Contains("1 queued", StringComparison.Ordinal)).IsTrue();

        var queueButtons = component.FindAll("button");
        queueButtons.First(button => button.TextContent.Contains("Send queued now", StringComparison.Ordinal)).Click();
        queueButtons.First(button => button.TextContent.Contains("Clear", StringComparison.Ordinal)).Click();

        await Assert.That(drainCalls).IsEqualTo(1);
        await Assert.That(clearCalls).IsEqualTo(1);
    }
}
