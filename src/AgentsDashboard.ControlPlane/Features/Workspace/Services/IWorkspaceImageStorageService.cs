namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public interface IWorkspaceImageStorageService
{
    Task<WorkspaceImageStoreResult> StoreAsync(
        string runId,
        string repositoryId,
        string taskId,
        IReadOnlyList<WorkspaceImageInput> images,
        CancellationToken cancellationToken);

    string BuildFallbackReferenceBlock(IReadOnlyList<WorkspaceStoredImage> images);
}

public sealed record WorkspaceImageStoreResult(
    bool Success,
    string Message,
    IReadOnlyList<WorkspaceStoredImage> Images);
