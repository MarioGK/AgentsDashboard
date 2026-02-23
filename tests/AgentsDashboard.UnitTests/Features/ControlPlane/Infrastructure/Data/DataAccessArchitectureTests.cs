using System.Text.RegularExpressions;

namespace AgentsDashboard.UnitTests.ControlPlane.Architecture;

public sealed class DataAccessArchitectureTests
{
    private static readonly Regex SqlitePattern = new("Sqlite|sqlite-vec|Microsoft\\.Data\\.Sqlite|UseSqlite", RegexOptions.Compiled);
    private static readonly Regex LiteDbUsingPattern = new("^using\\s+LiteDB;", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ScopePattern = new("\\bILiteDbScopeFactory\\b|\\bLiteDbScope\\b|\\bLiteDbScopeFactory\\b|\\bLiteDbSet<", RegexOptions.Compiled);
    private static readonly Regex AggregateStorePattern = new("\\bIControlPlaneStore\\b|\\bIOrchestratorStore\\b", RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedLiteDbUsingFiles =
    [
        "src/AgentsDashboard.ControlPlane/Infrastructure/Data/LiteDbDatabase.cs",
        "src/AgentsDashboard.ControlPlane/Infrastructure/Data/LiteDbExecutor.cs",
        "src/AgentsDashboard.ControlPlane/Infrastructure/Data/LiteDbRepository.cs",
        "src/AgentsDashboard.ControlPlane/Infrastructure/Data/RunArtifactStorageRepository.cs"
    ];

    private static readonly HashSet<string> RepositoryPatternServiceFiles =
    [
        "src/AgentsDashboard.ControlPlane/Features/Workspace/Services/WorkspaceImageStorageService.cs"
    ];

    [Test]
    public async Task RuntimeCode_DoesNotContainSqliteIdentifiers()
    {
        var matches = FindMatches(
            rootFolder: "src",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: SqlitePattern,
            pathFilter: _ => true);

        await Assert.That(matches).IsEmpty();
    }

    [Test]
    public async Task RuntimeCode_UsesLiteDbNamespaceOnlyInInfrastructure()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: LiteDbUsingPattern,
            pathFilter: path => !AllowedLiteDbUsingFiles.Contains(path));

        await Assert.That(matches).IsEmpty();
    }

    [Test]
    public async Task RuntimeCode_DoesNotReferenceCentralLiteDbScopeTypes()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: ScopePattern,
            pathFilter: _ => true);

        await Assert.That(matches).IsEmpty();
    }

    [Test]
    public async Task VectorAndWorkspaceServices_DoNotReferenceAggregateStoreContracts()
    {
        var matches = FindMatches(
            rootFolder: "src/AgentsDashboard.ControlPlane/Features",
            includeFile: path => path.EndsWith(".cs", StringComparison.Ordinal),
            pattern: AggregateStorePattern,
            pathFilter: RepositoryPatternServiceFiles.Contains);

        await Assert.That(matches).IsEmpty();
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
