using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceThreadMessageListTests
{
    [Test]
    public async Task MessageListRendersProvidedMessagesAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        var now = DateTime.UtcNow;
        var messages = new List<WorkspaceChatMessage>
        {
            new(
                Id: "msg-1",
                Kind: WorkspaceChatMessageKind.User,
                Title: "You",
                Content: "Initial prompt",
                TimestampUtc: now,
                Meta: string.Empty),
            new(
                Id: "msg-2",
                Kind: WorkspaceChatMessageKind.AssistantSummary,
                Title: "Run Summary",
                Content: "Execution succeeded.",
                TimestampUtc: now.AddSeconds(1),
                Meta: "Run 123")
        };

        var component = context.Render<WorkspaceThreadMessageList>(parameters => parameters
            .Add(p => p.Messages, messages));

        var stream = component.Find("[data-testid='workspace-chat-stream']");
        await Assert.That(stream.TextContent.Contains("Initial prompt", StringComparison.Ordinal)).IsTrue();
        await Assert.That(stream.TextContent.Contains("Execution succeeded.", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MessageListShowsCopyButtonForAllMessagesAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        var now = DateTime.UtcNow;
        var messages = new List<WorkspaceChatMessage>
        {
            new(
                Id: "copy-msg-1",
                Kind: WorkspaceChatMessageKind.User,
                Title: "You",
                Content: "First",
                TimestampUtc: now,
                Meta: string.Empty),
            new(
                Id: "copy-msg-2",
                Kind: WorkspaceChatMessageKind.System,
                Title: "System",
                Content: "Second",
                TimestampUtc: now.AddSeconds(1),
                Meta: string.Empty)
        };

        var component = context.Render<WorkspaceThreadMessageList>(parameters => parameters
            .Add(p => p.Messages, messages));

        component.Find("[data-testid='workspace-chat-copy-copy-msg-1']");
        component.Find("[data-testid='workspace-chat-copy-copy-msg-2']");

        await Assert.That(component.FindAll("[data-testid^='workspace-chat-copy-']").Count).IsEqualTo(2);
    }

    [Test]
    public async Task MessageListInvokesEditCallbackForEditablePromptMessagesAsync()
    {
        await using var context = WorkspaceBunitTestContext.Create();

        WorkspaceChatMessage? editedMessage = null;
        var now = DateTime.UtcNow;
        var message = new WorkspaceChatMessage(
            Id: "edit-msg-1",
            Kind: WorkspaceChatMessageKind.User,
            Title: "You",
            Content: "Needs edit",
            TimestampUtc: now,
            Meta: string.Empty,
            PromptEntryId: "prompt-1",
            IsEditable: true);

        var component = context.Render<WorkspaceThreadMessageList>(parameters => parameters
            .Add(p => p.Messages, new[] { message })
            .Add(p => p.OnEditMessageRequested, (WorkspaceChatMessage selectedMessage) => editedMessage = selectedMessage));

        component.Find("[data-testid='workspace-chat-edit-edit-msg-1']").Click();

        await Assert.That(editedMessage is not null).IsTrue();
        await Assert.That(editedMessage?.Id).IsEqualTo("edit-msg-1");
    }
}
