


namespace AgentsDashboard.ControlPlane.Features.Runs.Services;

public sealed class BlazorRunEventPublisher(IUiRealtimeBroker broker) : IRunEventPublisher
{
    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunStatusChangedEvent(
                run.Id,
                run.State.ToString(),
                run.Summary,
                run.StartedAtUtc,
                run.EndedAtUtc),
            cancellationToken);
    }

    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunLogChunkEvent(
                logEvent.RunId,
                logEvent.Level,
                logEvent.Message,
                logEvent.TimestampUtc),
            cancellationToken);
    }

    public Task PublishStructuredEventChangedAsync(
        string runId,
        long sequence,
        string category,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunStructuredEventChangedEvent(
                runId,
                sequence,
                category,
                payload,
                schema,
                timestampUtc),
            cancellationToken);
    }

    public Task PublishDiffUpdatedAsync(
        string runId,
        long sequence,
        string category,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunDiffUpdatedEvent(
                runId,
                sequence,
                category,
                payload,
                schema,
                timestampUtc),
            cancellationToken);
    }

    public Task PublishToolTimelineUpdatedAsync(
        string runId,
        long sequence,
        string category,
        string toolName,
        string toolCallId,
        string state,
        string payload,
        string schema,
        DateTime timestampUtc,
        CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunToolTimelineUpdatedEvent(
                runId,
                sequence,
                category,
                toolName,
                toolCallId,
                state,
                payload,
                schema,
                timestampUtc),
            cancellationToken);
    }

    public Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new FindingUpdatedEvent(
                finding.Id,
                finding.RepositoryId,
                finding.State.ToString(),
                finding.Severity.ToString(),
                finding.Title),
            cancellationToken);
    }

    public Task PublishTaskRuntimeHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new TaskRuntimeHeartbeatEvent(
                workerId,
                hostName,
                activeSlots,
                maxSlots,
                DateTime.UtcNow),
            cancellationToken);
    }

}
