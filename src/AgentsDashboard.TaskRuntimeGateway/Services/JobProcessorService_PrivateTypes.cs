using System.Text.Json;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway;
using AgentsDashboard.TaskRuntimeGateway.Models;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public partial class JobProcessorService
    ITaskRuntimeQueue queue,
    IHarnessExecutor executor,
    TaskRuntimeEventBus eventBus,
    ILogger<JobProcessorService> logger) : BackgroundService
{
    private sealed record StructuredProjection(
        string Category,
        string PayloadJson,
        string SchemaVersion);

    private sealed record RuntimeEventWireEnvelope(
        string Marker,
        long Sequence,
        string Type,
        string Content,
        Dictionary<string, string>? Metadata);
}
