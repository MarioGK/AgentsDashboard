using System.Text;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Components.Shared;



public static partial class RunDiffParser
{
    private static readonly Regex HunkRegex = HunkHeaderRegex();

    public static IReadOnlyList<RunDiffFileView> Parse(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch))
        {
            return [];
        }

        var lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var files = new List<RunDiffFileView>();
        DiffFileAccumulator? current = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    files.Add(current.ToView(files.Count + 1));
                }

                current = new DiffFileAccumulator();
                current.AppendPatchLine(line);
                current.SetPathsFromDiffHeader(line);
                continue;
            }

            if (current is null)
            {
                current = new DiffFileAccumulator();
            }

            current.AppendPatchLine(line);

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                current.OldPath = NormalizePath(line[4..]);
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                current.NewPath = NormalizePath(line[4..]);
                continue;
            }

            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                current.StartHunk(line);
                continue;
            }

            current.ProcessHunkLine(line);
        }

        if (current is not null)
        {
            files.Add(current.ToView(files.Count + 1));
        }

        return files;
    }

    private static string NormalizePath(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            trimmed = trimmed[1..^1];
        }

        trimmed = trimmed.Replace("\\\"", "\"", StringComparison.Ordinal);
        if (trimmed.StartsWith("a/", StringComparison.Ordinal) || trimmed.StartsWith("b/", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        return trimmed;
    }

    [GeneratedRegex(@"^@@\s*-(?<oldStart>\d+)(,(?<oldCount>\d+))?\s+\+(?<newStart>\d+)(,(?<newCount>\d+))?\s*@@\s*(?<header>.*)$", RegexOptions.Compiled)]
    private static partial Regex HunkHeaderRegex();

}
