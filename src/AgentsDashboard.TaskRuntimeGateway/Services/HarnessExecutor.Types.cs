using System.Text.Json;
using AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed partial class HarnessExecutor
{
    private sealed record RuntimeEventWireEnvelope(
        string Marker,
        long Sequence,
        string Type,
        string Content,
        IReadOnlyDictionary<string, string>? Metadata);

    private sealed record WorkspaceContext(
        string WorkspacePath,
        string MainBranch,
        string HeadBeforeRun);

    private sealed record GitCommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed class CallbackHarnessEventSink(
        Func<string, CancellationToken, Task> onLogChunk) : IHarnessEventSink
    {
        private long _sequence;

        public ValueTask PublishAsync(HarnessRuntimeEvent @event, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(@event.Content))
            {
                return ValueTask.CompletedTask;
            }

            var payload = JsonSerializer.Serialize(new RuntimeEventWireEnvelope(
                RuntimeEventWireMarker,
                Interlocked.Increment(ref _sequence),
                @event.Type.ToCanonicalName(),
                @event.Content,
                @event.Metadata));

            return new ValueTask(onLogChunk(payload, cancellationToken));
        }
    }
}
