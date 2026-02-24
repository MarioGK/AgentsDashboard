
using AgentsDashboard.ControlPlane.Components.Workspace.Models;


namespace AgentsDashboard.ControlPlane.Components.Workspace;

public sealed class WorkspaceChatProjectionBuilder
{
    public IReadOnlyList<WorkspaceChatMessage> Build(
        IReadOnlyList<WorkspacePromptEntryDocument> promptHistory,
        RunDocument? selectedRun,
        RunAiSummaryDocument? selectedRunAiSummary,
        ParsedHarnessOutput? selectedRunParsed,
        RunStructuredViewSnapshot selectedRunStructuredView,
        IReadOnlyList<RunLogEvent> selectedRunLogs)
    {
        var messages = new List<WorkspaceChatMessage>();

        foreach (var entry in promptHistory.OrderBy(x => x.CreatedAtUtc))
        {
            var normalizedRole = entry.Role?.Trim().ToLowerInvariant() ?? string.Empty;
            var kind = normalizedRole == "user"
                ? WorkspaceChatMessageKind.User
                : WorkspaceChatMessageKind.System;
            var title = kind == WorkspaceChatMessageKind.User ? "You" : normalizedRole switch
            {
                "assistant" => "Assistant",
                _ => string.IsNullOrWhiteSpace(entry.Role) ? "System" : entry.Role,
            };

            messages.Add(new WorkspaceChatMessage(
                Id: string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
                Kind: kind,
                Title: title,
                Content: entry.Content,
                TimestampUtc: entry.CreatedAtUtc,
                Meta: BuildPromptMeta(entry),
                PromptEntryId: entry.Id,
                IsEditable: kind == WorkspaceChatMessageKind.User,
                HasImages: entry.HasImages));
        }

        if (selectedRun is not null)
        {
            var summary = selectedRunAiSummary?.Summary;
            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = selectedRunParsed?.Summary;
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = selectedRun.Summary;
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                summary = BuildRunStateSummary(selectedRun.State);
            }

            var runEndedAt = selectedRun.EndedAtUtc ?? selectedRun.CreatedAtUtc;
            var runLabel = selectedRun.Id[..Math.Min(8, selectedRun.Id.Length)];
            messages.Add(new WorkspaceChatMessage(
                Id: $"run-summary-{selectedRun.Id}",
                Kind: WorkspaceChatMessageKind.AssistantSummary,
                Title: "Run Summary",
                Content: summary,
                TimestampUtc: runEndedAt,
                Meta: $"Run {runLabel} · {selectedRun.State}"));

            var eventContent = BuildEventSummary(selectedRunStructuredView, selectedRunLogs, selectedRunParsed);
            if (!string.IsNullOrWhiteSpace(eventContent))
            {
                messages.Add(new WorkspaceChatMessage(
                    Id: $"run-event-{selectedRun.Id}",
                    Kind: WorkspaceChatMessageKind.Event,
                    Title: "Runtime Events",
                    Content: eventContent,
                    TimestampUtc: runEndedAt,
                    Meta: "Thinking · Tools · Diff · Logs"));
            }
        }

        return messages
            .OrderBy(x => x.TimestampUtc)
            .ToList();
    }

    private static string BuildPromptMeta(WorkspacePromptEntryDocument entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.RunId))
        {
            parts.Add($"Run {entry.RunId[..Math.Min(8, entry.RunId.Length)]}");
        }

        if (entry.HasImages)
        {
            parts.Add("Includes images");
        }

        return parts.Count == 0
            ? string.Empty
            : string.Join(" · ", parts);
    }

    private static string BuildRunStateSummary(RunState state)
    {
        return state switch
        {
            RunState.Running => "Execution in progress.",
            RunState.Queued => "Execution is queued.",
            RunState.PendingApproval => "Execution is pending approval.",
            RunState.Succeeded => "Execution succeeded.",
            RunState.Failed => "Execution failed.",
            RunState.Cancelled => "Execution was cancelled.",
            _ => "Execution updated.",
        };
    }

    private static string BuildEventSummary(
        RunStructuredViewSnapshot selectedRunStructuredView,
        IReadOnlyList<RunLogEvent> selectedRunLogs,
        ParsedHarnessOutput? selectedRunParsed)
    {
        var fragments = new List<string>
        {
            $"Thinking: {selectedRunStructuredView.Thinking.Count}",
            $"Tools: {selectedRunStructuredView.Tools.Count}",
            $"Diff updates: {(selectedRunStructuredView.Diff is null ? 0 : 1)}",
            $"Log lines: {selectedRunLogs.Count}",
        };

        if (!string.IsNullOrWhiteSpace(selectedRunParsed?.Error))
        {
            fragments.Add($"Error: {Truncate(selectedRunParsed.Error, 120)}");
        }
        else if (selectedRunLogs.Count > 0)
        {
            fragments.Add($"Latest log: {Truncate(selectedRunLogs[^1].Message, 120)}");
        }

        return string.Join("\n", fragments.Where(fragment => !string.IsNullOrWhiteSpace(fragment)));
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength].TrimEnd() + "...";
    }
}
