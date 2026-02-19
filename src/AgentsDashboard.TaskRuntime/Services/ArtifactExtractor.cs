using System.Security.Cryptography;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntime.Configuration;
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

public sealed class ArtifactExtractor(
    ILogger<ArtifactExtractor> logger,
    IOptions<TaskRuntimeOptions> options) : IArtifactExtractor
{
    private static readonly HashSet<string> DefaultArtifactPatterns =
    [
        "*.patch",
        "*.diff",
        "*.md",
        "*.json",
        "*.yaml",
        "*.yml",
        "*.log",
        "*.txt",
        "*.xml",
        "*.html",
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.gif",
        "*.webp",
        "*.svg",
        "*.mp4",
        "*.webm",
        "*.zip",
        "*.tar",
        "*.gz",
        "*.har",
        "*.trace"
    ];

    private static readonly Dictionary<string, string> MimeTypes = new()
    {
        [".patch"] = "text/plain",
        [".diff"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".log"] = "text/plain",
        [".txt"] = "text/plain",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".zip"] = "application/zip",
        [".tar"] = "application/x-tar",
        [".gz"] = "application/gzip",
        [".har"] = "application/json",
        [".trace"] = "application/json"
    };

    private static readonly HashSet<string> ExcludedDirectories =
    [
        ".git",
        ".github",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build",
        ".venv",
        "venv",
        "__pycache__",
        ".idea",
        ".vscode"
    ];

    public async Task<List<ExtractedArtifact>> ExtractArtifactsAsync(
        string workspacePath,
        string runId,
        ArtifactPolicyConfig policy,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(workspacePath))
        {
            logger.LogWarning("Workspace path does not exist: {Path}", workspacePath);
            return [];
        }

        var artifactDir = Path.Combine(options.Value.ArtifactStoragePath, runId);
        Directory.CreateDirectory(artifactDir);

        var artifacts = new List<ExtractedArtifact>();
        var totalSize = 0L;
        var maxFiles = Math.Min(policy.MaxArtifacts, 100);

        var candidateFiles = FindCandidateFiles(workspacePath);
        candidateFiles = candidateFiles.OrderBy(f => f.Length).ToList();

        foreach (var file in candidateFiles)
        {
            if (artifacts.Count >= maxFiles)
            {
                logger.LogInformation("Artifact limit reached ({Max}), skipping remaining files", maxFiles);
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileInfo = new FileInfo(file);
                if (totalSize + fileInfo.Length > policy.MaxTotalSizeBytes)
                {
                    logger.LogInformation("Artifact size limit reached, skipping {File}", file);
                    break;
                }

                var relativePath = Path.GetRelativePath(workspacePath, file);
                var destinationPath = Path.Combine(artifactDir, relativePath);
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                File.Copy(file, destinationPath, overwrite: true);

                var checksum = await ComputeChecksumAsync(file, cancellationToken);
                var extension = Path.GetExtension(file).ToLowerInvariant();
                var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

                var artifact = new ExtractedArtifact
                {
                    FileName = Path.GetFileName(file),
                    SourcePath = file,
                    DestinationPath = destinationPath,
                    SizeBytes = fileInfo.Length,
                    Checksum = checksum,
                    MimeType = mimeType
                };

                artifacts.Add(artifact);
                totalSize += fileInfo.Length;

                logger.LogDebug("Extracted artifact: {File} ({Size} bytes)", relativePath, fileInfo.Length);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to extract artifact: {File}", file);
            }
        }

        logger.LogInformation("Extracted {Count} artifacts ({Size} bytes total) to {Dir}",
            artifacts.Count, totalSize, artifactDir);

        return artifacts;
    }

    private List<string> FindCandidateFiles(string workspacePath)
    {
        var files = new List<string>();

        try
        {
            foreach (var pattern in DefaultArtifactPatterns)
            {
                files.AddRange(Directory.GetFiles(workspacePath, pattern, SearchOption.AllDirectories));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied while searching for artifacts in {Path}", workspacePath);
        }

        files = files
            .Where(f => !ExcludedDirectories.Any(d => f.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar)))
            .Distinct()
            .ToList();

        return files;
    }

    private static async Task<string> ComputeChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
