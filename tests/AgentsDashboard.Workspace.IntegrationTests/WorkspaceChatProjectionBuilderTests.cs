using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Features.Runs.Services;
using AgentsDashboard.Contracts.Features.Runs.Models.Domain;
using AgentsDashboard.Contracts.Features.Shared.Models.Domain;
using AgentsDashboard.Contracts.Features.Workspace.Models.Domain;

namespace AgentsDashboard.Workspace.IntegrationTests;

public sealed class WorkspaceChatProjectionBuilderTests
{
    [Test]
    public async Task BuildIncludesPromptSummaryAndEventMessagesAsync()
    {
        var builder = new WorkspaceChatProjectionBuilder();
        var now = DateTime.UtcNow;

        var promptHistory = new List<WorkspacePromptEntryDocument>
        {
            new()
            {
                Id = "prompt-1",
                Role = "user",
                Content = "Create a task run",
                CreatedAtUtc = now
            }
        };

        var run = new RunDocument
        {
            Id = "run-12345678",
            State = RunState.Failed,
            CreatedAtUtc = now.AddSeconds(1),
            EndedAtUtc = now.AddSeconds(2)
        };

        var parsed = new ParsedHarnessOutput(
            ParsedEnvelope: false,
            Status: "failed",
            Summary: string.Empty,
            Error: "Tool execution failed",
            NormalizedOutputJson: string.Empty,
            Sections: [],
            ToolCallGroups: [],
            RawStream: []);

        var structuredView = new RunStructuredViewSnapshot(
            RunId: run.Id,
            LastSequence: 0,
            Timeline: [],
            Thinking: [],
            Tools: [],
            Diff: null,
            UpdatedAtUtc: now.AddSeconds(2));

        var logs = new List<RunLogEvent>
        {
            new()
            {
                RunId = run.Id,
                Level = "error",
                Message = "Tool execution failed",
                TimestampUtc = now.AddSeconds(2)
            }
        };

        var messages = builder.Build(
            promptHistory,
            run,
            selectedRunAiSummary: null,
            parsed,
            structuredView,
            logs);

        await Assert.That(messages.Any(message => message.Title == "You" && message.Content == "Create a task run")).IsTrue();
        await Assert.That(messages.Any(message => message.Title == "Run Summary" && message.Content.Contains("Execution failed.", StringComparison.Ordinal))).IsTrue();
        await Assert.That(messages.Any(message => message.Title == "Runtime Events" && message.Content.Contains("Log lines: 1", StringComparison.Ordinal))).IsTrue();
    }
}
