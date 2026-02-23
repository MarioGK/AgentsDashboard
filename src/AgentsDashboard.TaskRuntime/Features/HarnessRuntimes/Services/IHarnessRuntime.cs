namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public interface IHarnessRuntime
{
    string Name { get; }

    Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct);
}
