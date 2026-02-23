using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;
using AgentsDashboard.Workspace.ComponentTests.Infrastructure;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceThreadMessageListTests
{
    [Test]
    public async Task MessageListRendersProvidedMessagesAsync()
    {
        using var context = WorkspaceBunitTestContext.Create();

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

        var component = context.RenderComponent<WorkspaceThreadMessageList>(parameters => parameters
            .Add(p => p.Messages, messages));

        var stream = component.Find("[data-testid='workspace-chat-stream']");
        await Assert.That(stream.TextContent.Contains("Initial prompt", StringComparison.Ordinal)).IsTrue();
        await Assert.That(stream.TextContent.Contains("Execution succeeded.", StringComparison.Ordinal)).IsTrue();
    }
}
