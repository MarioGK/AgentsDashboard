namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public interface IHarnessRuntimeFactory
{
    HarnessRuntimeSelection Select(HarnessRunRequest request);
}
