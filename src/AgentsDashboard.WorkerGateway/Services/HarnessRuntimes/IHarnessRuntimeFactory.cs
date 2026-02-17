namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public interface IHarnessRuntimeFactory
{
    HarnessRuntimeSelection Select(HarnessRunRequest request);
}
