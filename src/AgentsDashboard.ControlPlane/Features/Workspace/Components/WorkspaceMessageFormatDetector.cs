using System.Text.RegularExpressions;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;

namespace AgentsDashboard.ControlPlane.Components.Workspace;

public sealed class WorkspaceMessageFormatDetector
{
    private static readonly Regex HtmlTagRegex = new(
        @"</?[a-zA-Z][^>]*>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public WorkspaceMessageFormat Detect(WorkspaceChatMessage message)
    {
        if (message.Kind is not (WorkspaceChatMessageKind.AssistantSummary or WorkspaceChatMessageKind.Event))
        {
            return WorkspaceMessageFormat.PlainText;
        }

        var content = message.Content?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            return WorkspaceMessageFormat.PlainText;
        }

        if (HtmlTagRegex.IsMatch(content))
        {
            return WorkspaceMessageFormat.Markdown;
        }

        if (content.Contains("```", StringComparison.Ordinal) ||
            content.StartsWith('#') ||
            content.StartsWith('>'))
        {
            return WorkspaceMessageFormat.Markdown;
        }

        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("- ", StringComparison.Ordinal) ||
                line.StartsWith("* ", StringComparison.Ordinal) ||
                line.StartsWith("+ ", StringComparison.Ordinal))
            {
                return WorkspaceMessageFormat.Markdown;
            }

            if (line.Length >= 3 &&
                char.IsDigit(line[0]) &&
                line[1] == '.' &&
                line[2] == ' ')
            {
                return WorkspaceMessageFormat.Markdown;
            }
        }

        var inlineScore = 0;
        if (content.Contains("[", StringComparison.Ordinal) &&
            content.Contains("](", StringComparison.Ordinal) &&
            content.Contains(')', StringComparison.Ordinal))
        {
            inlineScore += 2;
        }

        if (content.Contains("**", StringComparison.Ordinal) || content.Contains("__", StringComparison.Ordinal))
        {
            inlineScore++;
        }

        if (content.Contains('`'))
        {
            inlineScore++;
        }

        return inlineScore >= 2
            ? WorkspaceMessageFormat.Markdown
            : WorkspaceMessageFormat.PlainText;
    }
}
