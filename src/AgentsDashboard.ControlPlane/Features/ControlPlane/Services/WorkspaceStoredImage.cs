namespace AgentsDashboard.ControlPlane.Services;


public sealed record WorkspaceStoredImage(
    string Id,
    string FileName,
    string MimeType,
    long SizeBytes,
    string ArtifactName,
    string ArtifactPath,
    string Sha256,
    string DataUrl,
    int? Width,
    int? Height);
