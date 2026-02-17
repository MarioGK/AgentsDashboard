using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Components.Shared;

namespace AgentsDashboard.ControlPlane.Services;

public sealed record AgentTeamLaneDiffInput(
    string LaneLabel,
    string Harness,
    string RunId,
    bool Succeeded,
    string Summary,
    string DiffStat,
    string DiffPatch);

public static class AgentTeamDiffMergeService
{
    public static WorkflowAgentTeamDiffResult Build(IReadOnlyList<AgentTeamLaneDiffInput> laneInputs)
    {
        var result = new WorkflowAgentTeamDiffResult();
        if (laneInputs.Count == 0)
        {
            return result;
        }

        var laneContexts = new List<LaneContext>(laneInputs.Count);
        foreach (var input in laneInputs)
        {
            var files = RunDiffParser.Parse(input.DiffPatch);
            var laneDiff = new WorkflowAgentTeamLaneDiff
            {
                LaneLabel = input.LaneLabel,
                Harness = input.Harness,
                RunId = input.RunId,
                Succeeded = input.Succeeded,
                Summary = input.Summary,
                DiffStat = input.DiffStat,
                DiffPatch = input.DiffPatch,
                FilesChanged = files.Count,
                Additions = files.Sum(x => x.AddedLines),
                Deletions = files.Sum(x => x.RemovedLines),
                FilePaths = files.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
            result.LaneDiffs.Add(laneDiff);
            laneContexts.Add(new LaneContext(input, files));
        }

        var mergedPatches = new List<string>();
        var mergedFiles = 0;
        var mergedAdditions = 0;
        var mergedDeletions = 0;

        var fileGroups = laneContexts
            .SelectMany(context => context.Files.Select(file => new LaneFileChange(context.Input, file)))
            .GroupBy(x => x.File.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var fileGroup in fileGroups)
        {
            var changes = fileGroup.ToList();
            if (changes.Count == 1)
            {
                var change = changes[0];
                mergedFiles++;
                mergedAdditions += change.File.AddedLines;
                mergedDeletions += change.File.RemovedLines;
                mergedPatches.Add(change.File.Patch);
                continue;
            }

            var mergeOutcome = TryMergeSharedFile(fileGroup.Key, changes);
            if (mergeOutcome.Success)
            {
                mergedFiles++;
                mergedAdditions += mergeOutcome.Additions;
                mergedDeletions += mergeOutcome.Deletions;
                mergedPatches.Add(mergeOutcome.MergedPatch);
                continue;
            }

            result.Conflicts.Add(mergeOutcome.Conflict);
        }

        result.MergedFiles = mergedFiles;
        result.ConflictCount = result.Conflicts.Count;
        result.Additions = mergedAdditions;
        result.Deletions = mergedDeletions;
        result.MergedPatch = string.Join(
            "\n",
            mergedPatches.Where(static patch => !string.IsNullOrWhiteSpace(patch)).Select(static patch => patch.TrimEnd()));
        result.MergedDiffStat = BuildDiffStat(mergedFiles, mergedAdditions, mergedDeletions);
        return result;
    }

    private static MergeOutcome TryMergeSharedFile(string filePath, IReadOnlyList<LaneFileChange> changes)
    {
        var laneLabels = changes.Select(x => x.Input.LaneLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var first = changes[0].File;

        if (changes.Any(x => x.File.Hunks.Count == 0))
        {
            return MergeOutcome.FromConflict(
                filePath,
                "unable to merge metadata-only patch",
                laneLabels,
                []);
        }

        if (changes.Any(x =>
                !string.Equals(NormalizePath(x.File.OldPath), NormalizePath(first.OldPath), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(NormalizePath(x.File.NewPath), NormalizePath(first.NewPath), StringComparison.OrdinalIgnoreCase)))
        {
            return MergeOutcome.FromConflict(
                filePath,
                "incompatible path metadata",
                laneLabels,
                []);
        }

        var overlappingHeaders = FindOverlappingHunks(changes);
        if (overlappingHeaders.Count > 0)
        {
            return MergeOutcome.FromConflict(
                filePath,
                "overlapping hunks",
                laneLabels,
                overlappingHeaders);
        }

        if (!TryBuildMergedFilePatch(first, changes, out var mergedPatch))
        {
            return MergeOutcome.FromConflict(
                filePath,
                "failed to compose merged patch",
                laneLabels,
                []);
        }

        return MergeOutcome.FromMerged(
            mergedPatch,
            changes.Sum(x => x.File.AddedLines),
            changes.Sum(x => x.File.RemovedLines));
    }

    private static List<string> FindOverlappingHunks(IReadOnlyList<LaneFileChange> changes)
    {
        var overlappingHeaders = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < changes.Count; i++)
        {
            var left = BuildRanges(changes[i]);
            for (var j = i + 1; j < changes.Count; j++)
            {
                var right = BuildRanges(changes[j]);
                foreach (var leftRange in left)
                {
                    foreach (var rightRange in right)
                    {
                        if (!RangesOverlap(leftRange.Start, leftRange.End, rightRange.Start, rightRange.End))
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(leftRange.Header))
                        {
                            overlappingHeaders.Add(leftRange.Header);
                        }

                        if (!string.IsNullOrWhiteSpace(rightRange.Header))
                        {
                            overlappingHeaders.Add(rightRange.Header);
                        }
                    }
                }
            }
        }

        return overlappingHeaders.ToList();
    }

    private static List<HunkRange> BuildRanges(LaneFileChange change)
    {
        var ranges = new List<HunkRange>(change.File.Hunks.Count);
        foreach (var hunk in change.File.Hunks)
        {
            var start = Math.Max(1, hunk.NewStart);
            var width = Math.Max(1, hunk.NewCount);
            var end = start + width - 1;
            ranges.Add(new HunkRange(start, end, hunk.Header));
        }

        return ranges;
    }

    private static bool TryBuildMergedFilePatch(
        RunDiffFileView baseline,
        IReadOnlyList<LaneFileChange> changes,
        out string mergedPatch)
    {
        mergedPatch = string.Empty;

        var mergedBlocks = new List<MergedHunkBlock>();
        foreach (var change in changes)
        {
            var blockTexts = ExtractHunkBlocks(change.File.Patch);
            if (blockTexts.Count != change.File.Hunks.Count || blockTexts.Count == 0)
            {
                return false;
            }

            for (var index = 0; index < blockTexts.Count; index++)
            {
                var hunk = change.File.Hunks[index];
                mergedBlocks.Add(new MergedHunkBlock(
                    NewStart: Math.Max(1, hunk.NewStart),
                    Header: hunk.Header,
                    Text: blockTexts[index]));
            }
        }

        mergedBlocks = mergedBlocks
            .OrderBy(x => x.NewStart)
            .ThenBy(x => x.Header, StringComparer.Ordinal)
            .ToList();

        var oldPath = NormalizePath(baseline.OldPath);
        var newPath = NormalizePath(baseline.NewPath);
        var diffPath = ResolveDiffPath(oldPath, newPath, baseline.Path);

        var builder = new StringBuilder();
        builder.Append("diff --git a/");
        builder.Append(diffPath);
        builder.Append(" b/");
        builder.AppendLine(diffPath);
        builder.Append("--- ");
        builder.AppendLine(ToPatchPath(oldPath, "a/"));
        builder.Append("+++ ");
        builder.AppendLine(ToPatchPath(newPath, "b/"));

        foreach (var block in mergedBlocks)
        {
            builder.AppendLine(block.Text.TrimEnd('\r', '\n'));
        }

        mergedPatch = builder.ToString().TrimEnd();
        return !string.IsNullOrWhiteSpace(mergedPatch);
    }

    private static List<string> ExtractHunkBlocks(string patch)
    {
        var lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var blocks = new List<string>();
        StringBuilder? current = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    blocks.Add(current.ToString().TrimEnd('\r', '\n'));
                }

                current = new StringBuilder();
                current.AppendLine(line);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            current.AppendLine(line);
        }

        if (current is not null)
        {
            blocks.Add(current.ToString().TrimEnd('\r', '\n'));
        }

        return blocks;
    }

    private static bool RangesOverlap(int leftStart, int leftEnd, int rightStart, int rightEnd)
    {
        return leftStart <= rightEnd && rightStart <= leftEnd;
    }

    private static string BuildDiffStat(int files, int additions, int deletions)
    {
        if (files <= 0)
        {
            return string.Empty;
        }

        var parts = new List<string>
        {
            $"{files} file{(files == 1 ? string.Empty : "s")} changed"
        };

        if (additions > 0)
        {
            parts.Add($"{additions} insertion{(additions == 1 ? string.Empty : "s")}(+)");
        }

        if (deletions > 0)
        {
            parts.Add($"{deletions} deletion{(deletions == 1 ? string.Empty : "s")}(-)");
        }

        return string.Join(", ", parts);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim();
        if (normalized.StartsWith("a/", StringComparison.Ordinal) || normalized.StartsWith("b/", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string ResolveDiffPath(string oldPath, string newPath, string fallbackPath)
    {
        if (!string.IsNullOrWhiteSpace(newPath) && !string.Equals(newPath, "/dev/null", StringComparison.Ordinal))
        {
            return newPath;
        }

        if (!string.IsNullOrWhiteSpace(oldPath) && !string.Equals(oldPath, "/dev/null", StringComparison.Ordinal))
        {
            return oldPath;
        }

        return fallbackPath;
    }

    private static string ToPatchPath(string path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "/dev/null", StringComparison.Ordinal))
        {
            return "/dev/null";
        }

        return $"{prefix}{path}";
    }

    private sealed record LaneContext(
        AgentTeamLaneDiffInput Input,
        IReadOnlyList<RunDiffFileView> Files);

    private sealed record LaneFileChange(
        AgentTeamLaneDiffInput Input,
        RunDiffFileView File);

    private sealed record HunkRange(
        int Start,
        int End,
        string Header);

    private sealed record MergedHunkBlock(
        int NewStart,
        string Header,
        string Text);

    private sealed record MergeOutcome(
        bool Success,
        string MergedPatch,
        int Additions,
        int Deletions,
        WorkflowAgentTeamConflict Conflict)
    {
        public static MergeOutcome FromMerged(string patch, int additions, int deletions)
        {
            return new MergeOutcome(
                Success: true,
                MergedPatch: patch,
                Additions: additions,
                Deletions: deletions,
                Conflict: new WorkflowAgentTeamConflict());
        }

        public static MergeOutcome FromConflict(string filePath, string reason, List<string> laneLabels, List<string> hunkHeaders)
        {
            return new MergeOutcome(
                Success: false,
                MergedPatch: string.Empty,
                Additions: 0,
                Deletions: 0,
                Conflict: new WorkflowAgentTeamConflict
                {
                    FilePath = filePath,
                    Reason = reason,
                    LaneLabels = laneLabels,
                    HunkHeaders = hunkHeaders,
                });
        }
    }
}
