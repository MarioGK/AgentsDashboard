using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed partial class TaskRuntimeHarnessToolHealthService
{
    private sealed record ToolDefinition(string Command, string DisplayName);
}
