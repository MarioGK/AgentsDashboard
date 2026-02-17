namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public interface IHarnessRuntime
{
    string Name { get; }

    Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct);
}
