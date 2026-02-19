namespace AgentsDashboard.ControlPlane.Services;


public interface IHostFileExplorerService
{
    Task<IReadOnlyList<HostDirectoryEntry>> ListDirectoriesAsync(string path, CancellationToken cancellationToken);
    Task<string> CreateDirectoryAsync(string parentPath, string name, CancellationToken cancellationToken);
}

public sealed record HostDirectoryEntry(string Name, string FullPath, bool HasChildren);
