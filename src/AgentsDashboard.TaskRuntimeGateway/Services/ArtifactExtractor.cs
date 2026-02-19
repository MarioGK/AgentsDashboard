using System.Security.Cryptography;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using Microsoft.Extensions.Options;

public interface IArtifactExtractor
{
    Task<List<ExtractedArtifact>> ExtractArtifactsAsync(
        string workspacePath,
        string runId,
        ArtifactPolicyConfig policy,
        CancellationToken cancellationToken);
}


public sealed record ExtractedArtifact
{
    public required string FileName { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Checksum { get; init; }
    public string? MimeType { get; init; }
}
