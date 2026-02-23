namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

public interface IHarnessRuntimeFactory
{
    HarnessRuntimeSelection Select(HarnessRunRequest request);
}
