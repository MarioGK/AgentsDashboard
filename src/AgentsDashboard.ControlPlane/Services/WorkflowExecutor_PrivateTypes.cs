using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public partial class WorkflowExecutor
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IContainerReaper containerReaper,
    IOptions<OrchestratorOptions> options,
    ILogger<WorkflowExecutor> logger,
    TimeProvider? timeProvider = null) : IWorkflowExecutor
{
    private sealed record ParallelRunResult(
        bool Success,
        string Summary,
        string RunId,
        string LaneLabel,
        string Harness);
}
