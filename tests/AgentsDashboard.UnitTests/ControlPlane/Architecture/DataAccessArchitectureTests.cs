using System.Text.RegularExpressions;

namespace AgentsDashboard.UnitTests.ControlPlane.Architecture;

public sealed class DataAccessArchitectureTests
{
    private static readonly Regex SqlitePattern = new("Sqlite|sqlite-vec|Microsoft\\.Data\\.Sqlite|UseSqlite", RegexOptions.Compiled);
    private static readonly Regex LiteDbUsingPattern = new("^using\\s+LiteDB;", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ScopePattern = new("\\bILiteDbScopeFactory\\b|\\bLiteDbScope\\b|\\bLiteDbScopeFactory\\b|\\bLiteDbSet<", RegexOptions.Compiled);
    private static readonly Regex OrchestratorStorePattern = new("\\bIOrchestratorStore\\b", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedLiteDbUsingFiles =
    [
        "src/AgentsDashboard.ControlPlane/Data/LiteDbDatabase.cs",
        "src/AgentsDashboard.ControlPlane/Data/LiteDbExecutor.cs",
        "src/AgentsDashboard.ControlPlane/Data/LiteDbRepository.cs",
        "src/AgentsDashboard.ControlPlane/Data/RunArtifactStorageRepository.cs"
    ];

    private static readonly HashSet<string> RepositoryPatternServiceFiles =
    [
        "src/AgentsDashboard.ControlPlane/Services/TaskSemanticEmbeddingService.cs",
        "src/AgentsDashboard.ControlPlane/Services/GlobalSearchService.cs",
        "src/AgentsDashboard.ControlPlane/Services/WorkspaceImageStorageService.cs"
    ];

    [Test]
    public void RuntimeCode_DoesNotContainSqliteIdentifiers()
    {
        var matches = FindMatches(
            rootFolder: "src",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: SqlitePattern,
            pathFilter: _ => true);

        matches.Should().BeEmpty();
    }

    [Test]
    public void RuntimeCode_UsesLiteDbNamespaceOnlyInInfrastructure()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: LiteDbUsingPattern,
            pathFilter: path => !AllowedLiteDbUsingFiles.Contains(path));

        matches.Should().BeEmpty();
    }

    [Test]
    public void RuntimeCode_DoesNotReferenceCentralLiteDbScopeTypes()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: ScopePattern,
            pathFilter: _ => true);

        matches.Should().BeEmpty();
    }

    [Test]
    public void VectorAndWorkspaceServices_DoNotReferenceCentralOrchestratorStore()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane/Services",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: OrchestratorStorePattern,
            pathFilter: RepositoryPatternServiceFiles.Contains);

        matches.Should().BeEmpty();
    }

    private static List<string> FindMatches(
        string rootFolder,
        Func<string, bool> includeFile,
        Regex pattern,
        Func<string, bool> pathFilter)
    {
        var root = FindRepositoryRoot();
        var scanRoot = Path.Combine(root, rootFolder);
        if (!Directory.Exists(scanRoot))
        {
            return [];
        }

        var matches = new List<string>();
        foreach (var absolutePath in Directory.EnumerateFiles(scanRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');
            if (!includeFile(relativePath) || !pathFilter(relativePath))
            {
                continue;
            }

            if (relativePath.Contains("/bin/", StringComparison.Ordinal) || relativePath.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(absolutePath);
            if (pattern.IsMatch(content))
            {
                matches.Add(relativePath);
            }
        }

        return matches;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "AgentsDashboard.ControlPlane");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
