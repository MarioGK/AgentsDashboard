using System.Collections.Concurrent;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class RunStructuredViewService
{
    private sealed record AppliedDelta(
        RunStructuredDiffSnapshot? Diff,
        RunStructuredToolTimelineItem? Tool);

    private sealed class ProjectionState(string runId)
    {
        public string RunId { get; } = runId;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Hydrated { get; set; }
        public long LastSequence { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public HashSet<long> SeenSequences { get; } = [];
        public List<RunStructuredTimelineItem> Timeline { get; } = [];
        public List<RunStructuredThinkingItem> Thinking { get; } = [];
        public List<RunStructuredToolTimelineItem> Tools { get; } = [];
        public RunStructuredDiffSnapshot? Diff { get; set; }
    }
}
