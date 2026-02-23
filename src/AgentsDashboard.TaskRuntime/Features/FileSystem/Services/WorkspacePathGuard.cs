

namespace AgentsDashboard.TaskRuntime.Features.FileSystem.Services;

public sealed class WorkspacePathGuard(TaskRuntimeOptions options)
{
    private readonly string _workspaceRootPath = ResolveWorkspaceRootPath(options.WorkspacesRootPath);

    public string WorkspaceRootPath => _workspaceRootPath;

    public string ResolvePath(string path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? "."
            : path.Trim();

        var resolvedPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(Path.Combine(_workspaceRootPath, candidate));

        if (!IsWithinWorkspaceRoot(resolvedPath))
        {
            throw new InvalidOperationException($"Path '{path}' is outside workspace root.");
        }

        return resolvedPath;
    }

    public string ToRelativePath(string path)
    {
        var resolvedPath = ResolvePath(path);
        var relativePath = Path.GetRelativePath(_workspaceRootPath, resolvedPath);

        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
        {
            return ".";
        }

        return relativePath.Replace('\\', '/');
    }

    public bool IsWorkspaceRoot(string path)
    {
        var resolvedPath = ResolvePath(path);
        return string.Equals(resolvedPath, _workspaceRootPath, GetPathComparison());
    }

    private bool IsWithinWorkspaceRoot(string path)
    {
        var normalizedRoot = _workspaceRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _workspaceRootPath
            : _workspaceRootPath + Path.DirectorySeparatorChar;

        return string.Equals(path, _workspaceRootPath, GetPathComparison())
               || path.StartsWith(normalizedRoot, GetPathComparison())
               || path.StartsWith(_workspaceRootPath + Path.AltDirectorySeparatorChar, GetPathComparison());
    }

    private static string ResolveWorkspaceRootPath(string? configuredPath)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredPath)
            ? "/workspaces/repos"
            : configuredPath.Trim();

        var resolvedPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(candidate, Directory.GetCurrentDirectory());

        Directory.CreateDirectory(resolvedPath);
        return resolvedPath;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
