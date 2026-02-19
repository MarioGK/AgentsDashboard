using System.Collections.Concurrent;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;







public interface IRunStructuredViewService
{
    Task<RunStructuredProjectionDelta> ApplyStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken);
    Task<RunStructuredViewSnapshot> GetViewAsync(string runId, CancellationToken cancellationToken);
}

public sealed class RunStructuredViewService(IOrchestratorStore store) : IRunStructuredViewService
{
    private const int StructuredLoadLimit = 4000;
    private const int TimelineCap = 1200;
    private const int ThinkingCap = 400;
    private const int ToolCap = 600;
    private const int TimelineMessageCap = 360;

    private readonly ConcurrentDictionary<string, ProjectionState> _stateByRunId = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RunStructuredProjectionDelta> ApplyStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(structuredEvent.RunId))
        {
            return new RunStructuredProjectionDelta(CreateEmptySnapshot(string.Empty), null, null);
        }

        var state = GetOrCreateState(structuredEvent.RunId);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureHydratedAsync(state, cancellationToken);
            var applied = ApplyStructuredEventCore(state, structuredEvent);
            return new RunStructuredProjectionDelta(CreateSnapshot(state), applied.Diff, applied.Tool);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    public async Task<RunStructuredViewSnapshot> GetViewAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return CreateEmptySnapshot(string.Empty);
        }

        var state = GetOrCreateState(runId);
        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureHydratedAsync(state, cancellationToken);
            return CreateSnapshot(state);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private ProjectionState GetOrCreateState(string runId)
    {
        return _stateByRunId.GetOrAdd(
            runId,
            static value => new ProjectionState(value));
    }

    private async Task EnsureHydratedAsync(ProjectionState state, CancellationToken cancellationToken)
    {
        if (state.Hydrated)
        {
            return;
        }

        var events = await store.ListRunStructuredEventsAsync(state.RunId, StructuredLoadLimit, cancellationToken);
        foreach (var structuredEvent in events
                     .OrderBy(x => x.Sequence)
                     .ThenBy(x => x.CreatedAtUtc)
                     .ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            ApplyStructuredEventCore(state, structuredEvent);
        }

        state.Hydrated = true;
    }

    private static AppliedDelta ApplyStructuredEventCore(ProjectionState state, RunStructuredEventDocument structuredEvent)
    {
        if (!state.SeenSequences.Add(structuredEvent.Sequence))
        {
            return new AppliedDelta(null, null);
        }

        var decoded = RunStructuredEventCodec.Decode(structuredEvent);
        var timelineEntry = new RunStructuredTimelineItem(
            structuredEvent.Sequence,
            decoded.Category,
            BuildTimelineMessage(decoded),
            decoded.PayloadJson,
            decoded.Schema,
            decoded.TimestampUtc);
        state.Timeline.Add(timelineEntry);
        TrimToCap(state.Timeline, TimelineCap);

        if (TryBuildThinkingItem(structuredEvent.Sequence, decoded, out var thinkingItem))
        {
            state.Thinking.Add(thinkingItem);
            TrimToCap(state.Thinking, ThinkingCap);
        }

        RunStructuredToolTimelineItem? updatedTool = null;
        if (TryBuildToolItem(structuredEvent.Sequence, decoded, out var toolItem))
        {
            UpsertTool(state.Tools, toolItem);
            TrimToCap(state.Tools, ToolCap);
            updatedTool = toolItem;
        }

        RunStructuredDiffSnapshot? updatedDiff = null;
        if (TryBuildDiffSnapshot(structuredEvent.Sequence, decoded, out var diffSnapshot))
        {
            state.Diff = diffSnapshot;
            updatedDiff = diffSnapshot;
        }

        state.LastSequence = Math.Max(state.LastSequence, structuredEvent.Sequence);
        state.UpdatedAtUtc = decoded.TimestampUtc > state.UpdatedAtUtc ? decoded.TimestampUtc : state.UpdatedAtUtc;

        return new AppliedDelta(updatedDiff, updatedTool);
    }

    private static RunStructuredViewSnapshot CreateSnapshot(ProjectionState state)
    {
        return new RunStructuredViewSnapshot(
            state.RunId,
            state.LastSequence,
            state.Timeline.ToList(),
            state.Thinking.ToList(),
            state.Tools.ToList(),
            state.Diff,
            state.UpdatedAtUtc);
    }

    private static RunStructuredViewSnapshot CreateEmptySnapshot(string runId)
    {
        return new RunStructuredViewSnapshot(
            runId,
            0,
            [],
            [],
            [],
            null,
            DateTime.UtcNow);
    }

    private static string BuildTimelineMessage(DecodedRunStructuredEvent decoded)
    {
        var message = decoded.Summary;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = decoded.Error;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = decoded.PayloadJson;
        }

        message = message?.Trim() ?? string.Empty;
        if (message.Length <= TimelineMessageCap)
        {
            return message;
        }

        return message[..TimelineMessageCap];
    }

    private static bool TryBuildThinkingItem(long sequence, DecodedRunStructuredEvent decoded, out RunStructuredThinkingItem item)
    {
        item = default!;
        if (!IsThinkingCategory(decoded.Category, decoded.EventType) &&
            !HasPayloadProperty(decoded.PayloadJson, "thinking", "reasoning", "analysis"))
        {
            return false;
        }

        var content =
            ReadPayloadString(decoded.PayloadJson, "thinking", "reasoning", "analysis", "message", "content", "text");
        if (string.IsNullOrWhiteSpace(content))
        {
            content = decoded.Summary;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            content = decoded.PayloadJson;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        item = new RunStructuredThinkingItem(
            sequence,
            decoded.Category,
            content,
            decoded.TimestampUtc);
        return true;
    }

    private static bool TryBuildToolItem(long sequence, DecodedRunStructuredEvent decoded, out RunStructuredToolTimelineItem item)
    {
        item = default!;
        var toolName = ReadPayloadString(decoded.PayloadJson, "toolName", "tool_name", "tool", "name", "function", "function_name");
        var toolCallId = ReadPayloadString(decoded.PayloadJson, "toolCallId", "tool_call_id", "callId", "call_id", "id");
        var state = ReadPayloadString(decoded.PayloadJson, "state", "status", "phase");
        var isToolEvent = ContainsToken(decoded.Category, "tool") ||
                          ContainsToken(decoded.EventType, "tool") ||
                          !string.IsNullOrWhiteSpace(toolName) ||
                          !string.IsNullOrWhiteSpace(toolCallId);

        if (!isToolEvent)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            toolName = decoded.Category;
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            state = decoded.EventType;
        }

        item = new RunStructuredToolTimelineItem(
            sequence,
            decoded.Category,
            toolName,
            toolCallId,
            state,
            decoded.PayloadJson,
            decoded.Schema,
            decoded.TimestampUtc);
        return true;
    }

    private static bool TryBuildDiffSnapshot(long sequence, DecodedRunStructuredEvent decoded, out RunStructuredDiffSnapshot snapshot)
    {
        snapshot = default!;
        var diffPatch = ReadPayloadString(decoded.PayloadJson, "diffPatch", "diff_patch", "patch", "diff", "unifiedDiff", "unified_diff");
        var diffStat = ReadPayloadString(decoded.PayloadJson, "diffStat", "diff_stat", "stats");
        var summary = decoded.Summary;

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = ReadPayloadString(decoded.PayloadJson, "summary", "title");
        }

        var isDiffEvent = ContainsToken(decoded.Category, "diff") ||
                          ContainsToken(decoded.EventType, "diff") ||
                          !string.IsNullOrWhiteSpace(diffPatch) ||
                          !string.IsNullOrWhiteSpace(diffStat);

        if (!isDiffEvent)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(diffPatch))
        {
            diffPatch = decoded.PayloadJson;
        }

        snapshot = new RunStructuredDiffSnapshot(
            sequence,
            decoded.Category,
            summary ?? string.Empty,
            diffStat ?? string.Empty,
            diffPatch,
            decoded.PayloadJson,
            decoded.Schema,
            decoded.TimestampUtc);
        return true;
    }

    private static bool IsThinkingCategory(string category, string eventType)
    {
        return ContainsToken(category, "thinking") ||
               ContainsToken(category, "reasoning") ||
               ContainsToken(category, "analysis") ||
               ContainsToken(eventType, "thinking") ||
               ContainsToken(eventType, "reasoning") ||
               ContainsToken(eventType, "analysis");
    }

    private static bool ContainsToken(string candidate, string token)
    {
        return candidate.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPayloadProperty(string payloadJson, params string[] propertyNames)
    {
        return ReadPayloadString(payloadJson, propertyNames).Length > 0;
    }

    private static string ReadPayloadString(string payloadJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || propertyNames.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            foreach (var propertyName in propertyNames)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return property.Value.ValueKind switch
                    {
                        JsonValueKind.Null => string.Empty,
                        JsonValueKind.String => property.Value.GetString()?.Trim() ?? string.Empty,
                        _ => property.Value.GetRawText()
                    };
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static void UpsertTool(List<RunStructuredToolTimelineItem> tools, RunStructuredToolTimelineItem updated)
    {
        if (!string.IsNullOrWhiteSpace(updated.ToolCallId))
        {
            var existingIndex = tools.FindIndex(x => string.Equals(x.ToolCallId, updated.ToolCallId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                tools[existingIndex] = updated;
                return;
            }
        }

        tools.Add(updated);
    }

    private static void TrimToCap<T>(List<T> source, int cap)
    {
        if (source.Count <= cap)
        {
            return;
        }

        source.RemoveRange(0, source.Count - cap);
    }

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

    private sealed record AppliedDelta(
        RunStructuredDiffSnapshot? Diff,
        RunStructuredToolTimelineItem? Tool);
}
