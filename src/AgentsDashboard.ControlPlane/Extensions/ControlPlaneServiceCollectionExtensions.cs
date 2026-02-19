using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion.Serialization.MessagePack;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;
using MudBlazor.Services;

namespace AgentsDashboard.ControlPlane;

internal static class ControlPlaneServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneServices(this IServiceCollection services, bool isDevelopment)
    {
        services
            .AddDataLayerServices(isDevelopment)
            .AddRuntimeOrchestrationServices()
            .AddWorkflowServices()
            .AddGatewayAndUiServices()
            .AddRpcClientServices();

        return services;
    }

    private static IServiceCollection AddDataLayerServices(this IServiceCollection services, bool isDevelopment)
    {
        services.AddSingleton<LiteDbDatabase>();
        services.AddHostedService(sp => sp.GetRequiredService<LiteDbDatabase>());
        services.AddSingleton<LiteDbExecutor>();
        services.AddSingleton<ILiteDbCollectionNameResolver, LiteDbCollectionNameResolver>();
        services.AddSingleton(typeof(IRepository<>), typeof(LiteDbRepository<>));
        services.AddSingleton<ISemanticChunkRepository, SemanticChunkRepository>();
        services.AddSingleton<IRunArtifactStorage, RunArtifactStorageRepository>();
        if (isDevelopment)
        {
            services.AddHostedService<DevelopmentSelfRepositoryBootstrapService>();
        }

        services.AddSingleton<IOrchestratorRepositorySessionFactory, OrchestratorRepositorySessionFactory>();
        services.AddSingleton<OrchestratorStore>();
        services.AddSingleton<IOrchestratorStore>(sp => sp.GetRequiredService<OrchestratorStore>());
        services.AddSingleton<ILiteDbVectorSearchStatusService, LiteDbVectorSearchStatusService>();
        services.AddSingleton<ILiteDbVectorBootstrapService>(sp =>
            (LiteDbVectorSearchStatusService)sp.GetRequiredService<ILiteDbVectorSearchStatusService>());
        services.AddHostedService(sp => (LiteDbVectorSearchStatusService)sp.GetRequiredService<ILiteDbVectorSearchStatusService>());
        return services;
    }

    private static IServiceCollection AddRuntimeOrchestrationServices(this IServiceCollection services)
    {
        services.AddSingleton<RunDispatcher>();
        services.AddSingleton<IBackgroundWorkCoordinator, BackgroundWorkScheduler>();
        services.AddHostedService(sp => (BackgroundWorkScheduler)sp.GetRequiredService<IBackgroundWorkCoordinator>());
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<INotificationService>());
        services.AddHostedService<BackgroundWorkNotificationRelay>();
        services.AddSingleton<IOrchestratorRuntimeSettingsProvider, OrchestratorRuntimeSettingsProvider>();
        services.AddSingleton<ILeaseCoordinator, LeaseCoordinator>();
        services.AddSingleton<ITaskRuntimeLifecycleManager, DockerTaskRuntimeLifecycleManager>();
        services.AddHostedService<TaskRuntimeImageBootstrapService>();
        services.AddSingleton<ISecretCryptoService, SecretCryptoService>();
        services.AddSingleton<SecretCryptoService>(sp => (SecretCryptoService)sp.GetRequiredService<ISecretCryptoService>());
        services.AddSingleton<WebhookService>();
        services.AddSingleton<IUiRealtimeBroker, UiRealtimeBroker>();
        services.AddSingleton<IRunEventPublisher, BlazorRunEventPublisher>();
        services.AddSingleton<IRunStructuredViewService, RunStructuredViewService>();
        services.AddSingleton<IOrchestratorMetrics, OrchestratorMetrics>();
        services.AddHostedService<RecoveryService>();
        services.AddHostedService<CronSchedulerService>();
        services.AddHostedService<AutomationSchedulerService>();
        services.AddHostedService<TaskRetentionCleanupService>();
        services.AddHostedService<TaskRuntimeEventListenerService>();
        services.AddHostedService<TaskRuntimeIdleShutdownService>();
        services.AddHostedService<TaskRuntimePoolReconciliationService>();
        services.AddHostedService<AlertingService>();
        services.AddSingleton<TaskRuntimeCommandGateway>();
        services.AddSingleton<TaskRuntimeFileSystemGateway>();
        return services;
    }

    private static IServiceCollection AddWorkflowServices(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
        services.AddSingleton<WorkflowExecutor>(sp => (WorkflowExecutor)sp.GetRequiredService<IWorkflowExecutor>());
        services.AddSingleton<LlmTornadoGatewayService>();
        services.AddSingleton<IHarnessOutputParserService, HarnessOutputParserService>();
        services.AddSingleton<IWorkspaceAiService, WorkspaceAiService>();
        services.AddSingleton<IWorkspaceImageCompressionService, WorkspaceImageCompressionService>();
        services.AddSingleton<IWorkspaceImageStorageService, WorkspaceImageStorageService>();
        services.AddSingleton<IGlobalSearchService, GlobalSearchService>();
        services.AddSingleton<ITaskSemanticEmbeddingService, TaskSemanticEmbeddingService>();
        services.AddHostedService(sp => (TaskSemanticEmbeddingService)sp.GetRequiredService<ITaskSemanticEmbeddingService>());
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<IGitWorkspaceService, GitWorkspaceService>();
        services.AddSingleton<IHostFileExplorerService, HostFileExplorerService>();
        services.AddSingleton<ImageBuilderService>();
        services.AddSingleton<CredentialValidationService>();
        services.AddSingleton<TaskTemplateService>();
        services.AddHostedService<TaskTemplateInitializationService>();
        services.AddHostedService<RepositoryGitRefreshService>();
        services.AddSingleton<IContainerReaper, ContainerReaper>();

        services.AddScoped<ILocalStorageService, LocalStorageService>();
        services.AddScoped<IGlobalSelectionService, GlobalSelectionService>();
        return services;
    }

    private static IServiceCollection AddGatewayAndUiServices(this IServiceCollection services)
    {
        services.AddMudServices(config =>
        {
            config.PopoverOptions.CheckForPopoverProvider = false;
        });

        services.AddRazorComponents().AddInteractiveServerComponents();
        return services;
    }

    private static IServiceCollection AddRpcClientServices(this IServiceCollection services)
    {
        var messagePackOptions = MessagePackSerializerOptions.Standard
            .WithResolver(StandardResolver.Instance);

        services.AddMagicOnion(options =>
        {
            options.MessageSerializer = MessagePackMagicOnionSerializerProvider.Default.WithOptions(messagePackOptions);
        });

        services.AddSingleton<IMagicOnionClientFactory, MagicOnionClientFactory>();
        services.AddSingleton<ITaskRuntimeRegistryService, TaskRuntimeRegistryService>();
        return services;
    }
}
