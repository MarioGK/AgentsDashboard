namespace AgentsDashboard.ControlPlane.Services;


public sealed record WorkspaceImageInput(
    string Id,
    string FileName,
    string MimeType,
    long SizeBytes,
    string DataUrl,
    int? Width = null,
    int? Height = null);
