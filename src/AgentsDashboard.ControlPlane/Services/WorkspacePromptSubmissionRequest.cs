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









public sealed record WorkspacePromptSubmissionRequest(
    string Prompt,
    string? TaskId = null,
    string? Harness = null,
    string? Command = null,
    bool ForceNewRun = false,
    string? UserMessage = null,
    HarnessExecutionMode? ModeOverride = null,
    WorkspaceAgentTeamRequest? AgentTeam = null,
    IReadOnlyList<WorkspaceImageInput>? Images = null,
    bool PreferNativeMultimodal = true,
    string? SessionProfileId = null);
