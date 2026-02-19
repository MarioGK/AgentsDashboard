using System.IO;
using System.Text.Json;
using System.Threading.RateLimiting;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Middleware;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion.Serialization.MessagePack;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Yarp.ReverseProxy.Configuration;
using ZLogger;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredContainerLogging("ControlPlane");

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), ["live", "ready"])
    .AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);
builder.Services.AddOptions<OrchestratorOptions>()
    .Bind(builder.Configuration.GetSection(OrchestratorOptions.SectionName))
    .PostConfigure(options =>
    {
        options.LiteDbPath = RepositoryPathResolver.ResolveDataPath(
            options.LiteDbPath,
            OrchestratorOptions.DefaultLiteDbPath);
        options.ArtifactsRootPath = RepositoryPathResolver.ResolveDataPath(
            options.ArtifactsRootPath,
            OrchestratorOptions.DefaultArtifactsRootPath);
    })
    .ValidateOnStart();

var startupOptions = builder.Configuration.GetSection(OrchestratorOptions.SectionName).Get<OrchestratorOptions>();

if (startupOptions is not null)
{
    EnsureLiteDbDirectoryExists(startupOptions.LiteDbPath);
    EnsureArtifactsDirectoryExists(startupOptions.ArtifactsRootPath);
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("WebhookPolicy", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IOptions<OrchestratorOptions>>().Value.RateLimit;
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = config.WebhookPermitLimit,
                Window = TimeSpan.FromSeconds(config.WebhookWindowSeconds),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("GlobalPolicy", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IOptions<OrchestratorOptions>>().Value.RateLimit;
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = config.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(config.GlobalWindowSeconds),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.AddPolicy("BurstPolicy", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IOptions<OrchestratorOptions>>().Value.RateLimit;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.BurstPermitLimit,
                Window = TimeSpan.FromSeconds(config.BurstWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers["Retry-After"] = "60";

        if (context.Lease.TryGetMetadata("RateLimit-Reset", out var reset))
        {
            context.HttpContext.Response.Headers["Retry-After"] = reset?.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            message = "Rate limit exceeded. Please retry later.",
            statusCode = 429
        }, cancellationToken);
    };
});

var messagePackOptions = MessagePackSerializerOptions.Standard
    .WithResolver(StandardResolver.Instance);

builder.Services.AddMagicOnion(options =>
{
    options.MessageSerializer = MessagePackMagicOnionSerializerProvider.Default.WithOptions(messagePackOptions);
});

builder.Services.AddTransient<RateLimitHeadersMiddleware>();
builder.Services.AddSingleton<LiteDbDatabase>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiteDbDatabase>());
builder.Services.AddSingleton<LiteDbExecutor>();
builder.Services.AddSingleton<ILiteDbCollectionNameResolver, LiteDbCollectionNameResolver>();
builder.Services.AddSingleton(typeof(IRepository<>), typeof(LiteDbRepository<>));
builder.Services.AddSingleton<IRunArtifactStorage, RunArtifactStorageRepository>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<DevelopmentSelfRepositoryBootstrapService>();
}
builder.Services.AddSingleton<OrchestratorStore>();
builder.Services.AddSingleton<IOrchestratorStore>(sp => sp.GetRequiredService<OrchestratorStore>());
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddSingleton<IBackgroundWorkCoordinator, BackgroundWorkScheduler>();
builder.Services.AddHostedService(sp => (BackgroundWorkScheduler)sp.GetRequiredService<IBackgroundWorkCoordinator>());
builder.Services.AddSingleton<ILiteDbVectorSearchStatusService, LiteDbVectorSearchStatusService>();
builder.Services.AddHostedService(sp => (LiteDbVectorSearchStatusService)sp.GetRequiredService<ILiteDbVectorSearchStatusService>());
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddSingleton<INotificationSink>(sp => sp.GetRequiredService<INotificationService>());
builder.Services.AddHostedService<BackgroundWorkNotificationRelay>();
builder.Services.AddSingleton<IOrchestratorRuntimeSettingsProvider, OrchestratorRuntimeSettingsProvider>();
builder.Services.AddSingleton<ILeaseCoordinator, LeaseCoordinator>();
builder.Services.AddSingleton<ITaskRuntimeLifecycleManager, DockerTaskRuntimeLifecycleManager>();
builder.Services.AddHostedService<TaskRuntimeImageBootstrapService>();
builder.Services.AddSingleton<ISecretCryptoService, SecretCryptoService>();
builder.Services.AddSingleton<SecretCryptoService>(sp => (SecretCryptoService)sp.GetRequiredService<ISecretCryptoService>());
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<IUiRealtimeBroker, UiRealtimeBroker>();
builder.Services.AddSingleton<IRunEventPublisher, BlazorRunEventPublisher>();
builder.Services.AddSingleton<IRunStructuredViewService, RunStructuredViewService>();
builder.Services.AddSingleton<IOrchestratorMetrics, OrchestratorMetrics>();
builder.Services.AddHostedService<RecoveryService>();
builder.Services.AddHostedService<CronSchedulerService>();
builder.Services.AddHostedService<AutomationSchedulerService>();
builder.Services.AddHostedService<TaskRetentionCleanupService>();
builder.Services.AddHostedService<TaskRuntimeEventListenerService>();
builder.Services.AddHostedService<TaskRuntimeIdleShutdownService>();
builder.Services.AddHostedService<TaskRuntimePoolReconciliationService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AlertingService>();
builder.Services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
builder.Services.AddSingleton<WorkflowExecutor>(sp => (WorkflowExecutor)sp.GetRequiredService<IWorkflowExecutor>());
builder.Services.AddSingleton<LlmTornadoGatewayService>();
builder.Services.AddSingleton<IHarnessOutputParserService, HarnessOutputParserService>();
builder.Services.AddSingleton<IWorkspaceAiService, WorkspaceAiService>();
builder.Services.AddSingleton<IWorkspaceImageCompressionService, WorkspaceImageCompressionService>();
builder.Services.AddSingleton<IWorkspaceImageStorageService, WorkspaceImageStorageService>();
builder.Services.AddSingleton<IGlobalSearchService, GlobalSearchService>();
builder.Services.AddSingleton<ITaskSemanticEmbeddingService, TaskSemanticEmbeddingService>();
builder.Services.AddHostedService(sp => (TaskSemanticEmbeddingService)sp.GetRequiredService<ITaskSemanticEmbeddingService>());
builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();
builder.Services.AddSingleton<IGitWorkspaceService, GitWorkspaceService>();
builder.Services.AddSingleton<IHostFileExplorerService, HostFileExplorerService>();
builder.Services.AddSingleton<ImageBuilderService>();
builder.Services.AddSingleton<CredentialValidationService>();
builder.Services.AddSingleton<TaskTemplateService>();
builder.Services.AddHostedService<TaskTemplateInitializationService>();
builder.Services.AddHostedService<RepositoryGitRefreshService>();
builder.Services.AddSingleton<IContainerReaper, ContainerReaper>();
builder.Services.AddTransient<ProxyAuditMiddleware>();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddScoped<IGlobalSelectionService, GlobalSelectionService>();

// MagicOnion client factory for TaskRuntimeGateway communication (must be registered before ITaskRuntimeRegistryService)
builder.Services.AddSingleton<IMagicOnionClientFactory, MagicOnionClientFactory>();

// Task runtime registry service (depends on IMagicOnionClientFactory)
builder.Services.AddSingleton<ITaskRuntimeRegistryService, TaskRuntimeRegistryService>();

builder.Services.AddSingleton<InMemoryYarpConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryYarpConfigProvider>());
builder.Services.AddReverseProxy();

builder.Services.AddMudServices(config =>
{
    config.PopoverOptions.CheckForPopoverProvider = false;
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (app.Urls.Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
{
    app.UseHttpsRedirection();
}
app.UseRateLimiter();
app.UseMiddleware<RateLimitHeadersMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets();
app.UseMiddleware<ProxyAuditMiddleware>();
app.MapMagicOnionService();
app.MapReverseProxy();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var readyHealthCheckOptions = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponseAsync
};
var liveHealthCheckOptions = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = WriteHealthResponseAsync
};

app.MapHealthChecks("/health", readyHealthCheckOptions);
app.MapHealthChecks("/ready", readyHealthCheckOptions);
app.MapHealthChecks("/alive", liveHealthCheckOptions);

app.Run();

static void EnsureArtifactsDirectoryExists(string? artifactsRootPath)
{
    if (string.IsNullOrWhiteSpace(artifactsRootPath))
        return;

    var resolvedPath = RepositoryPathResolver.ResolveDataPath(
        artifactsRootPath,
        OrchestratorOptions.DefaultArtifactsRootPath);

    if (resolvedPath == ":memory:")
        return;

    Directory.CreateDirectory(resolvedPath);
}

static void EnsureLiteDbDirectoryExists(string? liteDbPath)
{
    if (string.IsNullOrWhiteSpace(liteDbPath))
        return;

    var resolvedPath = RepositoryPathResolver.ResolveDataPath(
        liteDbPath,
        OrchestratorOptions.DefaultLiteDbPath);

    if (resolvedPath == ":memory:")
        return;

    var directory = Path.GetDirectoryName(resolvedPath);
    if (string.IsNullOrWhiteSpace(directory))
        return;

    Directory.CreateDirectory(directory);
}

static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => new
            {
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
                description = entry.Value.Description,
                error = entry.Value.Exception?.Message
            })
    };

    return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
}

namespace AgentsDashboard.ControlPlane
{
    public partial class Program { }
}
