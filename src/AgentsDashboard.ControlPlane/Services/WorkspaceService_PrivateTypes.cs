using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class WorkspaceService
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IWorkflowExecutor workflowExecutor,
    IHarnessOutputParserService parserService,
    IWorkspaceAiService workspaceAiService,
    IWorkspaceImageStorageService workspaceImageStorageService,
    ITaskSemanticEmbeddingService taskSemanticEmbeddingService,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    private sealed class SummaryRefreshState
    {
        public object SyncRoot { get; } = new();
        public DateTime? LastEventAtUtc { get; set; }
        public DateTime? LastRefreshAtUtc { get; set; }
        public string LastObservedSignature { get; set; } = string.Empty;
        public string LastRefreshedSignature { get; set; } = string.Empty;
        public string LastSummary { get; set; } = string.Empty;
        public string LastEventType { get; set; } = string.Empty;
    }
}
