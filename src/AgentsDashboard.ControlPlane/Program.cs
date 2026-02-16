using System.Threading.RateLimiting;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Endpoints;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Middleware;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Yarp.ReverseProxy.Configuration;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOptions<OrchestratorOptions>()
    .Bind(builder.Configuration.GetSection(OrchestratorOptions.SectionName))
    .ValidateOnStart();

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

builder.Services.AddTransient<RateLimitHeadersMiddleware>();

builder.Services.AddDbContextFactory<OrchestratorDbContext>((sp, options) =>
{
    var orchestratorOptions = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
    options.UseSqlite(orchestratorOptions.SqliteConnectionString);
});

builder.Services.AddHostedService<DbMigrationHostedService>();
builder.Services.AddSingleton<IOrchestratorStore, OrchestratorStore>();
builder.Services.AddSingleton<OrchestratorStore>(sp => (OrchestratorStore)sp.GetRequiredService<IOrchestratorStore>());
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddSingleton<IWorkerLifecycleManager, DockerWorkerLifecycleManager>();
builder.Services.AddSingleton<ISecretCryptoService, SecretCryptoService>();
builder.Services.AddSingleton<SecretCryptoService>(sp => (SecretCryptoService)sp.GetRequiredService<ISecretCryptoService>());
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<IRunEventPublisher, SignalRRunEventPublisher>();
builder.Services.AddSingleton<IOrchestratorMetrics, OrchestratorMetrics>();
builder.Services.AddHostedService<RecoveryService>();
builder.Services.AddHostedService<CronSchedulerService>();
builder.Services.AddHostedService<WorkerEventListenerService>();
builder.Services.AddHostedService<WorkerIdleShutdownService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AlertingService>();
builder.Services.AddSingleton<IWorkflowExecutor, WorkflowExecutor>();
builder.Services.AddSingleton<WorkflowExecutor>(sp => (WorkflowExecutor)sp.GetRequiredService<IWorkflowExecutor>());
builder.Services.AddSingleton<IWorkflowDagExecutor, WorkflowDagExecutor>();
builder.Services.AddSingleton<ImageBuilderService>();
builder.Services.AddSingleton<CredentialValidationService>();
builder.Services.AddSingleton<TaskTemplateService>();
builder.Services.AddHostedService<TaskTemplateInitializationService>();
builder.Services.AddSingleton<IContainerReaper, ContainerReaper>();
builder.Services.AddSingleton<HarnessHealthService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HarnessHealthService>());
builder.Services.AddTransient<ProxyAuditMiddleware>();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddScoped<ProjectContext>();
builder.Services.AddScoped<IGlobalSelectionService, GlobalSelectionService>();

// MagicOnion client factory for WorkerGateway communication (must be registered before IWorkerRegistryService)
builder.Services.AddSingleton<IMagicOnionClientFactory, MagicOnionClientFactory>();

// Worker registry service (depends on IMagicOnionClientFactory)
builder.Services.AddSingleton<IWorkerRegistryService, WorkerRegistryService>();

// Terminal bridge service (implementation to be provided in another task)
// builder.Services.AddSingleton<ITerminalBridgeService, TerminalBridgeService>();

builder.Services.AddSingleton<InMemoryYarpConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryYarpConfigProvider>());
builder.Services.AddReverseProxy();

builder.Services.AddMudServices();
builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddEndpointsApiExplorer();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "AI Orchestrator API",
            Version = "v1",
            Description = "Self-hosted AI orchestration platform for harness execution (Codex, OpenCode, Claude Code, Zai)",
            Contact = new Microsoft.OpenApi.Models.OpenApiContact
            {
                Name = "AI Orchestrator",
                Email = "support@example.com"
            }
        });
    });
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/docs/{documentName}/swagger.json";
    });
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/api/docs/v1/swagger.json", "AI Orchestrator API v1");
        options.RoutePrefix = "api/docs";
        options.DocumentTitle = "AI Orchestrator API Documentation";
    });
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseMiddleware<RateLimitHeadersMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<RunEventsHub>("/hubs/runs");
app.MapHub<TerminalHub>("/hubs/terminal");
app.MapOrchestratorApi();
app.MapDagWorkflowApi();
app.UseMiddleware<ProxyAuditMiddleware>();
app.MapReverseProxy();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();

namespace AgentsDashboard.ControlPlane
{
    public partial class Program { }
}
