using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using ICSharpCode.SharpZipLib.Tar;

namespace AgentsDashboard.ControlPlane.Services;

public record ImageBuildResult(bool Success, string ImageId, List<string> Logs);
public record ImageInfo(string Tag, string Id, long Size, DateTime Created);

public class ImageBuilderService(ILogger<ImageBuilderService> logger) : IAsyncDisposable
{
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();

    public async Task<ImageBuildResult> BuildImageAsync(
        string dockerfileContent,
        string tag,
        Action<string>? onLogLine,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var imageId = string.Empty;

        try
        {
            logger.LogInformation("Building Docker image {Tag}", tag);

            var tarStream = CreateTarArchive(dockerfileContent);
            tarStream.Position = 0;

            var buildParameters = new ImageBuildParameters
            {
                Tags = [tag],
                Dockerfile = "Dockerfile",
                Remove = true,
                ForceRemove = true
            };

            var progress = new Progress<JSONMessage>(message =>
            {
                var logLine = message.Stream ?? message.Status ?? message.ErrorMessage ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(logLine))
                {
                    logLine = logLine.TrimEnd('\n', '\r');
                    logs.Add(logLine);
                    onLogLine?.Invoke(logLine);

                    if (message.Aux is not null)
                    {
                        try
                        {
                            var auxJson = JsonSerializer.Serialize(message.Aux);
                            var auxDict = JsonSerializer.Deserialize<Dictionary<string, object>>(auxJson);
                            if (auxDict?.TryGetValue("ID", out var idObj) == true && idObj is JsonElement elem)
                            {
                                imageId = elem.GetString() ?? string.Empty;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            });

            await _dockerClient.Images.BuildImageFromDockerfileAsync(
                buildParameters,
                tarStream,
                [],
                new Dictionary<string, string>(),
                progress,
                cancellationToken);

            if (string.IsNullOrEmpty(imageId))
            {
                imageId = tag;
            }

            var success = !logs.Any(l => l.Contains("error", StringComparison.OrdinalIgnoreCase));
            logger.LogInformation("Build completed for {Tag}. Success: {Success}, ImageId: {ImageId}", tag, success, imageId);

            return new ImageBuildResult(success, imageId, logs);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during image build for {Tag}", tag);
            logs.Add($"Error: {ex.Message}");
            return new ImageBuildResult(false, string.Empty, logs);
        }
    }

    public virtual async Task<List<ImageInfo>> ListImagesAsync(string? filter, CancellationToken cancellationToken)
    {
        try
        {
            var parameters = new ImagesListParameters
            {
                All = false
            };

            var images = await _dockerClient.Images.ListImagesAsync(parameters, cancellationToken);

            var result = images
                .SelectMany(img => img.RepoTags ?? [],
                    (img, tag) => new ImageInfo(
                        Tag: tag,
                        Id: img.ID,
                        Size: img.Size,
                        Created: img.Created))
                .Where(img => !img.Tag.EndsWith("<none>:<none>"))
                .Where(img => string.IsNullOrEmpty(filter) || img.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(img => img.Created)
                .ToList();

            logger.LogInformation("Listed {Count} Docker images", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while listing images");
            return [];
        }
    }

    public virtual async Task<bool> DeleteImageAsync(string tag, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Deleting Docker image {Tag}", tag);

            await _dockerClient.Images.DeleteImageAsync(
                tag,
                new ImageDeleteParameters { Force = true },
                cancellationToken);

            logger.LogInformation("Successfully deleted image {Tag}", tag);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete image {Tag}", tag);
            return false;
        }
    }

    public async Task<bool> TagImageAsync(string sourceTag, string targetTag, CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Tagging image {SourceTag} as {TargetTag}", sourceTag, targetTag);

            var parts = targetTag.Split(':', 2);
            var repo = parts.Length > 0 ? parts[0] : targetTag;
            var tag = parts.Length > 1 ? parts[1] : "latest";

            await _dockerClient.Images.TagImageAsync(
                sourceTag,
                new ImageTagParameters { RepositoryName = repo, Tag = tag },
                cancellationToken);

            logger.LogInformation("Successfully tagged image {SourceTag} as {TargetTag}", sourceTag, targetTag);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to tag image {SourceTag} as {TargetTag}", sourceTag, targetTag);
            return false;
        }
    }

    private static MemoryStream CreateTarArchive(string dockerfileContent)
    {
        var tarStream = new MemoryStream();
        using (var archive = new TarOutputStream(tarStream, Encoding.UTF8) { IsStreamOwner = false })
        {
            var dockerfileBytes = Encoding.UTF8.GetBytes(dockerfileContent);
            var entry = TarEntry.CreateTarEntry("Dockerfile");
            entry.Size = dockerfileBytes.Length;
            entry.ModTime = DateTime.UtcNow;

            archive.PutNextEntry(entry);
            archive.Write(dockerfileBytes, 0, dockerfileBytes.Length);
            archive.CloseEntry();
        }

        return tarStream;
    }

    public ValueTask DisposeAsync()
    {
        _dockerClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
