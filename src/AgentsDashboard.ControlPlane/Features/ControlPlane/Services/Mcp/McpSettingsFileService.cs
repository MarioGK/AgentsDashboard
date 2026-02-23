using System.Text;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class McpSettingsFileService
{
    private const string RelativePath = "config/mcp/system.mcp.json";

    public string GetPath()
    {
        return RepositoryPathResolver.GetDataPath(RelativePath);
    }

    public async Task<string> ReadAsync(CancellationToken cancellationToken)
    {
        var path = GetPath();
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task WriteAsync(string json, CancellationToken cancellationToken)
    {
        var path = GetPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
    }
}
