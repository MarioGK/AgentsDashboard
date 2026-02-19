using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

public sealed class HarnessRuntimeResult
{
    public required HarnessResultEnvelope Envelope { get; init; }
    public int ExitCode { get; init; }
    public bool Structured { get; init; }
}
