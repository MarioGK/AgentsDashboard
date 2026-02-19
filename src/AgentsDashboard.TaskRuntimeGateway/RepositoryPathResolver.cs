using System.IO;

namespace AgentsDashboard.TaskRuntimeGateway;

internal static class RepositoryPathResolver
{
    public static string ResolveDataPath(string? configuredPath, string defaultRelativePath)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultRelativePath
            : configuredPath.Trim();

        if (string.Equals(candidate, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (Path.IsPathRooted(candidate))
        {
            return Path.GetFullPath(candidate);
        }

        var relativePath = IsDataRelativePath(candidate)
            ? candidate
            : Path.Combine("data", candidate);

        return Path.GetFullPath(relativePath, FindRepositoryRoot());
    }

    public static string GetDataPath(params string[] segments)
    {
        var path = "data";
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                continue;
            }

            path = Path.Combine(path, segment);
        }

        return ResolveDataPath(path, "data");
    }

    private static bool IsDataRelativePath(string value)
    {
        if (string.Equals(value, "data", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.StartsWith("data/", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith($"data{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
               || value.StartsWith($"data{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in EnumerateStartDirectories())
        {
            var root = TryFindRepositoryRoot(start);
            if (!string.IsNullOrWhiteSpace(root))
            {
                return root;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static IEnumerable<string> EnumerateStartDirectories()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
    }

    private static string? TryFindRepositoryRoot(string start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, "src", "AgentsDashboard.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
