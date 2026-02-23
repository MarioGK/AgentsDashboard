

using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.ControlPlane.Data;

public sealed class OrchestratorStoreIntegrationFixture : IAsyncDisposable
{
    private readonly string _rootPath;
    private readonly ServiceProvider _serviceProvider;

    private OrchestratorStoreIntegrationFixture(
        string rootPath,
        ServiceProvider serviceProvider,
        IRepositoryStore repositoryStore,
        ITaskStore taskStore,
        IRunStore runStore)
    {
        _rootPath = rootPath;
        _serviceProvider = serviceProvider;
        RepositoryStore = repositoryStore;
        TaskStore = taskStore;
        RunStore = runStore;
    }

    public IRepositoryStore RepositoryStore { get; }

    public ITaskStore TaskStore { get; }

    public IRunStore RunStore { get; }

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
        services.AddSingleton<IOrchestratorRepositorySessionFactory, OrchestratorRepositorySessionFactory>();
        services.AddSingleton<IRunArtifactStorage, RunArtifactStorageRepository>();
        services.AddSingleton<IRepositoryStore, RepositoryStore>();
        services.AddSingleton<ITaskStore, TaskStore>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IRuntimeStore, RuntimeStore>();
        services.AddSingleton<ISystemStore, SystemStore>();

        var serviceProvider = services.BuildServiceProvider();
        var systemStore = serviceProvider.GetRequiredService<ISystemStore>();
        await systemStore.InitializeAsync(CancellationToken.None);
        var repositoryStore = serviceProvider.GetRequiredService<IRepositoryStore>();
        var taskStore = serviceProvider.GetRequiredService<ITaskStore>();
        var runStore = serviceProvider.GetRequiredService<IRunStore>();

        return new OrchestratorStoreIntegrationFixture(rootPath, serviceProvider, repositoryStore, taskStore, runStore);
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
