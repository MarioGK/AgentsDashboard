using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public sealed class WorkspaceImageCompressionService : IWorkspaceImageCompressionService
{
    public async Task<WorkspaceCompressedImage> CompressAsync(
        string mimeType,
        byte[] sourceBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mimeType) || !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported image MIME type '{mimeType}'.");
        }

        if (sourceBytes.Length == 0)
        {
            throw new InvalidOperationException("Image payload is empty.");
        }

        await using var source = new MemoryStream(sourceBytes, writable: false);
        using var image = await Image.LoadAsync(source, cancellationToken);
        await using var compressed = new MemoryStream();

        await image.SaveAsync(
            compressed,
            new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossless
            },
            cancellationToken);

        var compressedBytes = compressed.ToArray();
        if (compressedBytes.Length == 0)
        {
            throw new InvalidOperationException("Compressed image payload is empty.");
        }

        return new WorkspaceCompressedImage(
            "image/webp",
            compressedBytes,
            image.Width,
            image.Height);
    }
}
