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

public sealed record RunStructuredThinkingItem(
    long Sequence,
    string Category,
    string Content,
    DateTime TimestampUtc);
