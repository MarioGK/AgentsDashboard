namespace AgentsDashboard.ControlPlane.Services;

public sealed record WorkspaceCompressedImage(
    string MimeType,
    byte[] Bytes,
    int Width,
    int Height);
