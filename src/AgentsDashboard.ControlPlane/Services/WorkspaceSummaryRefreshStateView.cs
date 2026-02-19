using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkspaceService
{
    Task<WorkspacePageData?> GetWorkspacePageDataAsync(
        string repositoryId,
        string? selectedRunId,
        CancellationToken cancellationToken);

    Task<WorkspacePromptSubmissionResult> SubmitPromptAsync(
        string repositoryId,
        WorkspacePromptSubmissionRequest request,
        CancellationToken cancellationToken);

    void NotifyRunEvent(string runId, string eventType, DateTime eventTimestampUtc);

    Task<WorkspaceSummaryRefreshResult> RefreshRunSummaryAsync(
        string repositoryId,
        string runId,
        string eventType,
        bool force,
        CancellationToken cancellationToken);
}









public sealed record WorkspaceSummaryRefreshStateView(
    bool ShouldRefresh,
    string Reason,
    DateTime? LastRefreshAtUtc,
    DateTime? NextAllowedRefreshAtUtc,
    bool HasNewEvents);
