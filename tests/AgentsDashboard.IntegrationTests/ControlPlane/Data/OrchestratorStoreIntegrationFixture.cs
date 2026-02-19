using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.ControlPlane.Data;

public sealed class OrchestratorStoreIntegrationFixture : IAsyncDisposable
{
    private readonly string _rootPath;
    private readonly ServiceProvider _serviceProvider;

    private OrchestratorStoreIntegrationFixture(string rootPath, ServiceProvider serviceProvider, OrchestratorStore store)
    {
        _rootPath = rootPath;
        _serviceProvider = serviceProvider;
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

        var databasePath = Path.Combine(rootPath, "litedb", "orchestrator.db");
        var artifactsPath = Path.Combine(rootPath, "artifacts");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        Directory.CreateDirectory(artifactsPath);

        var services = new ServiceCollection();
        services.AddOptions<OrchestratorOptions>()
            .Configure(options =>
            {
                options.LiteDbPath = databasePath;
                options.ArtifactsRootPath = artifactsPath;
            });
        services.AddSingleton<LiteDbDatabase>();
        services.AddSingleton<LiteDbExecutor>();
        services.AddSingleton<ILiteDbCollectionNameResolver, LiteDbCollectionNameResolver>();
        services.AddSingleton(typeof(IRepository<>), typeof(LiteDbRepository<>));
        services.AddSingleton<IRunArtifactStorage, RunArtifactStorageRepository>();
        services.AddSingleton<OrchestratorStore>();

        var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<OrchestratorStore>();
        await store.InitializeAsync(CancellationToken.None);

        return new OrchestratorStoreIntegrationFixture(rootPath, serviceProvider, store);
    }

    public async ValueTask DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();

        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
