using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Components.Shared;

namespace AgentsDashboard.ControlPlane.Services;

public static partial class AgentTeamDiffMergeService
{
    private sealed record HunkRange(
        int Start,
        int End,
        string Header);

    private sealed record LaneContext(
        AgentTeamLaneDiffInput Input,
        IReadOnlyList<RunDiffFileView> Files);

    private sealed record LaneFileChange(
        AgentTeamLaneDiffInput Input,
        RunDiffFileView File);

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

    private sealed record MergedHunkBlock(
        int NewStart,
        string Header,
        string Text);
}
