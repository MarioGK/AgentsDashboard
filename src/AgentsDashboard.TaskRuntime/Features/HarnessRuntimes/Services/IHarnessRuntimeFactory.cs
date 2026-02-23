namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public interface IHarnessRuntimeFactory
{
    HarnessRuntimeSelection Select(HarnessRunRequest request);
}
