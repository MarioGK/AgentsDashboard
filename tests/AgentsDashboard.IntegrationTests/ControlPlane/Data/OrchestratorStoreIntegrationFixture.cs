using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.IntegrationTests.ControlPlane.Data;

public sealed class OrchestratorStoreIntegrationFixture : IAsyncDisposable
{
    private readonly string _rootPath;

    private OrchestratorStoreIntegrationFixture(string rootPath, OrchestratorStore store)
    {
        _rootPath = rootPath;
        Store = store;
    }

    public OrchestratorStore Store { get; }

    public static async Task<OrchestratorStoreIntegrationFixture> CreateAsync()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "agentsdashboard-integration-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(rootPath);

        var databasePath = Path.Combine(rootPath, "orchestrator.db");
        var artifactsPath = Path.Combine(rootPath, "artifacts");
        Directory.CreateDirectory(artifactsPath);

        var connectionString = $"Data Source={databasePath}";
        var dbContextOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var dbContextFactory = new TestDbContextFactory(dbContextOptions);

        await using (var db = await dbContextFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var store = new OrchestratorStore(
            dbContextFactory,
            Options.Create(new OrchestratorOptions
            {
                SqliteConnectionString = connectionString,
                ArtifactsRootPath = artifactsPath
            }));

        return new OrchestratorStoreIntegrationFixture(rootPath, store);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class TestDbContextFactory(DbContextOptions<OrchestratorDbContext> options) : IDbContextFactory<OrchestratorDbContext>
    {
        public OrchestratorDbContext CreateDbContext() => new(options);

        public Task<OrchestratorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OrchestratorDbContext(options));
        }
    }
}
