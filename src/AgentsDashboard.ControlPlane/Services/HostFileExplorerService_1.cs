namespace AgentsDashboard.ControlPlane.Services;


public interface IHostFileExplorerService
{
    Task<IReadOnlyList<HostDirectoryEntry>> ListDirectoriesAsync(string path, CancellationToken cancellationToken);
    Task<string> CreateDirectoryAsync(string parentPath, string name, CancellationToken cancellationToken);
}

public sealed class HostFileExplorerService : IHostFileExplorerService
{
    public Task<IReadOnlyList<HostDirectoryEntry>> ListDirectoriesAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedPath = ResolvePath(path);
        if (!Directory.Exists(resolvedPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {resolvedPath}");
        }

        var entries = Directory.EnumerateDirectories(resolvedPath)
            .Select(directory => new DirectoryInfo(directory))
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(info => new HostDirectoryEntry(info.Name, info.FullName, HasChildren(info.FullName)))
            .ToList();

        return Task.FromResult<IReadOnlyList<HostDirectoryEntry>>(entries);
    }

    public Task<string> CreateDirectoryAsync(string parentPath, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedParentPath = ResolvePath(parentPath);
        if (!Directory.Exists(resolvedParentPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {resolvedParentPath}");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Folder name is required.");
        }

        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Folder name cannot contain path separators.");
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("Folder name contains invalid characters.");
        }

        var combinedPath = Path.GetFullPath(Path.Combine(resolvedParentPath, name));
        if (!combinedPath.StartsWith(resolvedParentPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Folder path escapes selected parent directory.");
        }

        var created = Directory.CreateDirectory(combinedPath);
        return Task.FromResult(created.FullName);
    }

    private static string ResolvePath(string? path)
    {
        var selectedPath = string.IsNullOrWhiteSpace(path)
            ? Path.GetPathRoot(Environment.CurrentDirectory) ?? "/"
            : path;

        if (!Path.IsPathRooted(selectedPath))
        {
            throw new InvalidOperationException("Path must be absolute.");
        }

        return Path.GetFullPath(selectedPath);
    }

    private static bool HasChildren(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch
        {
            return false;
        }
    }
}
