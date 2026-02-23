namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public sealed record WorkspaceCompressedImage(
    string MimeType,
    byte[] Bytes,
    int Width,
    int Height);
