using System.Security.Cryptography;
using System.Text;


namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public sealed class WorkspaceImageStorageService(
    IRunArtifactStorage artifactStorage,
    IWorkspaceImageCompressionService imageCompressionService,
    ILogger<WorkspaceImageStorageService> logger) : IWorkspaceImageStorageService
{
    private const int MaxImageCount = 6;
    private const long MaxImageBytes = 8L * 1024L * 1024L;

    private static readonly Dictionary<string, string> s_extensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = ".png",
        ["image/jpeg"] = ".jpg",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif",
    };

    public async Task<WorkspaceImageStoreResult> StoreAsync(
        string runId,
        string repositoryId,
        string taskId,
        IReadOnlyList<WorkspaceImageInput> images,
        CancellationToken cancellationToken)
    {
        if (images.Count == 0)
        {
            return new WorkspaceImageStoreResult(true, string.Empty, []);
        }

        if (images.Count > MaxImageCount)
        {
            return new WorkspaceImageStoreResult(
                false,
                $"Too many images. Maximum is {MaxImageCount}.",
                []);
        }

        var stored = new List<WorkspaceStoredImage>(images.Count);

        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var inputMimeType = image.MimeType ?? string.Empty;
            if (!s_extensionByMime.ContainsKey(inputMimeType))
            {
                return new WorkspaceImageStoreResult(
                    false,
                    $"Unsupported image type '{image.MimeType}'.",
                    []);
            }

            if (!TryDecodeDataUrl(image.DataUrl, out var bytes))
            {
                return new WorkspaceImageStoreResult(
                    false,
                    $"Failed to decode image '{image.FileName}'.",
                    []);
            }

            if (bytes.Length == 0)
            {
                return new WorkspaceImageStoreResult(false, $"Image '{image.FileName}' is empty.", []);
            }

            WorkspaceCompressedImage compressed;
            try
            {
                compressed = await imageCompressionService.CompressAsync(inputMimeType, bytes, cancellationToken);
            }
            catch
            {
                return new WorkspaceImageStoreResult(
                    false,
                    $"Failed to compress image '{image.FileName}'.",
                    []);
            }

            if (!s_extensionByMime.TryGetValue(compressed.MimeType, out var extension))
            {
                return new WorkspaceImageStoreResult(
                    false,
                    $"Unsupported compressed image type '{compressed.MimeType}'.",
                    []);
            }

            if (compressed.Bytes.Length > MaxImageBytes)
            {
                return new WorkspaceImageStoreResult(
                    false,
                    $"Image '{image.FileName}' exceeds {(MaxImageBytes / (1024 * 1024))} MB.",
                    []);
            }

            var normalizedId = string.IsNullOrWhiteSpace(image.Id)
                ? Guid.NewGuid().ToString("N")
                : image.Id.Trim();
            var safeFileName = GetSafeFileName(image.FileName, index + 1);
            var artifactName = $"workspace-image-{index + 1:D2}-{normalizedId[..Math.Min(8, normalizedId.Length)]}{extension}";

            await using var stream = new MemoryStream(compressed.Bytes, writable: false);
            await artifactStorage.SaveAsync(runId, artifactName, stream, cancellationToken);

            var hash = Convert.ToHexString(SHA256.HashData(compressed.Bytes));
            stored.Add(new WorkspaceStoredImage(
                normalizedId,
                safeFileName,
                compressed.MimeType,
                compressed.Bytes.Length,
                artifactName,
                $"/artifacts/{runId}/{artifactName}",
                hash,
                BuildDataUrl(compressed.MimeType, compressed.Bytes),
                compressed.Width > 0 ? compressed.Width : image.Width,
                compressed.Height > 0 ? compressed.Height : image.Height));
        }

        logger.LogInformation(
            "Stored workspace images for run {RunId} repository {RepositoryId} task {TaskId}. Count: {Count}",
            runId,
            repositoryId,
            taskId,
            stored.Count);

        return new WorkspaceImageStoreResult(true, string.Empty, stored);
    }

    public string BuildFallbackReferenceBlock(IReadOnlyList<WorkspaceStoredImage> images)
    {
        if (images.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Attached images:");

        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var sizeKb = Math.Max(1, (int)Math.Round(image.SizeBytes / 1024d));
            var dimensions = image.Width is > 0 && image.Height is > 0
                ? $", {image.Width}x{image.Height}"
                : string.Empty;

            builder.Append("- Image ")
                .Append(index + 1)
                .Append(": ")
                .Append(image.FileName)
                .Append(" (")
                .Append(image.MimeType)
                .Append(dimensions)
                .Append(", ")
                .Append(sizeKb)
                .Append(" KB)")
                .Append(" at ")
                .Append(image.ArtifactPath)
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string GetSafeFileName(string fileName, int index)
    {
        var candidate = Path.GetFileName(fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return $"image-{index}";
        }

        return candidate;
    }

    private static bool TryDecodeDataUrl(string dataUrl, out byte[] bytes)
    {
        bytes = [];

        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            return false;
        }

        var commaIndex = dataUrl.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex < 0 || commaIndex == dataUrl.Length - 1)
        {
            return false;
        }

        var payload = dataUrl[(commaIndex + 1)..].Trim();

        try
        {
            bytes = Convert.FromBase64String(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildDataUrl(string mimeType, byte[] bytes)
    {
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }
}
