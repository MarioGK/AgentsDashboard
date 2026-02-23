

namespace AgentsDashboard.ControlPlane.Features.Runs.Services;

public interface IRunEventPublisher
{
    Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken);
    Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken);
    Task PublishStructuredEventChangedAsync(
        string runId,
        long sequence,
        string category,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken);
    Task PublishDiffUpdatedAsync(
        string runId,
        long sequence,
        string category,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken);
    Task PublishToolTimelineUpdatedAsync(
        string runId,
        long sequence,
        string category,
        string toolName,
        string toolCallId,
        string state,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken);
    Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken);
    Task PublishTaskRuntimeHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken);
}
