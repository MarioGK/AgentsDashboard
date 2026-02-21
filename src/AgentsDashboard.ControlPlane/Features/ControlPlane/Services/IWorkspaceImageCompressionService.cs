namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkspaceImageCompressionService
{
    Task<WorkspaceCompressedImage> CompressAsync(
        string mimeType,
        byte[] sourceBytes,
        CancellationToken cancellationToken);
}
