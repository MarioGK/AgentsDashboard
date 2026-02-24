using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;

namespace AgentsDashboard.Workspace.ComponentTests;

public sealed class WorkspaceMessageFormatDetectorTests
{
    [Test]
    public async Task DetectReturnsMarkdownForAssistantMarkdownSyntaxAsync()
    {
        var detector = new WorkspaceMessageFormatDetector();
        var message = new WorkspaceChatMessage(
            Id: "assistant-md",
            Kind: WorkspaceChatMessageKind.AssistantSummary,
            Title: "Assistant",
            Content: "## Run Summary\n- item",
            TimestampUtc: DateTime.UtcNow,
            Meta: string.Empty);

        var format = detector.Detect(message);
        await Assert.That(format).IsEqualTo(WorkspaceMessageFormat.Markdown);
    }

    [Test]
    public async Task DetectReturnsPlainTextForUserMarkdownSyntaxAsync()
    {
        var detector = new WorkspaceMessageFormatDetector();
        var message = new WorkspaceChatMessage(
            Id: "user-md",
            Kind: WorkspaceChatMessageKind.User,
            Title: "You",
            Content: "## User markdown should stay plain",
            TimestampUtc: DateTime.UtcNow,
            Meta: string.Empty);

        var format = detector.Detect(message);
        await Assert.That(format).IsEqualTo(WorkspaceMessageFormat.PlainText);
    }

    [Test]
    public async Task DetectReturnsMarkdownForEventHtmlAsync()
    {
        var detector = new WorkspaceMessageFormatDetector();
        var message = new WorkspaceChatMessage(
            Id: "event-html",
            Kind: WorkspaceChatMessageKind.Event,
            Title: "Runtime Events",
            Content: "<ul><li>Event</li></ul>",
            TimestampUtc: DateTime.UtcNow,
            Meta: string.Empty);

        var format = detector.Detect(message);
        await Assert.That(format).IsEqualTo(WorkspaceMessageFormat.Markdown);
    }
}
