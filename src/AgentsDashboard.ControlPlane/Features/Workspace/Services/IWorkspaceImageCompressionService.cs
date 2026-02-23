namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public interface IWorkspaceImageCompressionService
{
    Task<WorkspaceCompressedImage> CompressAsync(
        string mimeType,
        byte[] sourceBytes,
        CancellationToken cancellationToken);
}
