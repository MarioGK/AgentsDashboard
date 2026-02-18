namespace AgentsDashboard.TaskRuntimeGateway.Adapters;

public sealed class HarnessArtifact
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Type { get; init; } = "file";
    public long SizeBytes { get; init; }
    public string? MimeType { get; init; }
    public string? Checksum { get; init; }
}
