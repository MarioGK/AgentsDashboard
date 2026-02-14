using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Auth;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Endpoints;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Middleware;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using Swashbuckle.AspNetCore.SwaggerUI;
using Yarp.ReverseProxy.Configuration;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOptions<OrchestratorOptions>()
    .Bind(builder.Configuration.GetSection(OrchestratorOptions.SectionName))
    .ValidateOnStart();
builder.Services.Configure<DashboardAuthOptions>(builder.Configuration.GetSection(DashboardAuthOptions.SectionName));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("AuthPolicy", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IOptions<OrchestratorOptions>>().Value.RateLimit;
        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = config.AuthPermitLimit,
                Window = TimeSpan.FromSeconds(config.AuthWindowSeconds),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });

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
        var user = httpContext.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        var partitionKey = isAuthenticated
            ? user!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user!.Identity?.Name ?? "authenticated"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: partitionKey,
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
        var user = httpContext.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        var partitionKey = isAuthenticated
            ? user!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user!.Identity?.Name ?? "authenticated"
            : httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
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

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddAuthentication("Test")
        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { })
        .AddCookie();

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("viewer", policy => policy.RequireAuthenticatedUser());
        options.AddPolicy("operator", policy => policy.RequireAuthenticatedUser());
    });
}
else
{
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login";
            options.SlidingExpiration = true;
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("viewer", policy => policy.RequireRole("viewer", "operator", "admin"));
        options.AddPolicy("operator", policy => policy.RequireRole("operator", "admin"));
    });
}

builder.Services.AddDbContextFactory<OrchestratorDbContext>((sp, options) =>
{
    var orchestratorOptions = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
    options.UseSqlite(orchestratorOptions.SqliteConnectionString);
});

builder.Services.AddSingleton<IOrchestratorStore, OrchestratorStore>();
builder.Services.AddSingleton<OrchestratorStore>(sp => (OrchestratorStore)sp.GetRequiredService<IOrchestratorStore>());
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddSingleton<IWorkerLifecycleManager, DockerWorkerLifecycleManager>();
builder.Services.AddSingleton<ISecretCryptoService, SecretCryptoService>();
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
builder.Services.AddHostedService<DbMigrationHostedService>();
builder.Services.AddSingleton<IContainerReaper, ContainerReaper>();
builder.Services.AddSingleton<HarnessHealthService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HarnessHealthService>());
builder.Services.AddTransient<ProxyAuditMiddleware>();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddScoped<ProjectContext>();
builder.Services.AddScoped<IGlobalSelectionService, GlobalSelectionService>();

builder.Services.AddGrpcClient<WorkerGateway.WorkerGatewayClient>((sp, options) =>
    {
        var orchestratorOptions = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
        options.Address = new Uri(orchestratorOptions.WorkerGrpcAddress);
    })
    .ConfigureChannel(options =>
    {
        options.HttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
        };
    });

builder.Services.AddSingleton<InMemoryYarpConfigProvider>();
builder.Services.AddSingleton<IProxyConfigProvider>(sp => sp.GetRequiredService<InMemoryYarpConfigProvider>());
builder.Services.AddReverseProxy();

builder.Services.AddMudServices();
builder.Services.AddSignalR();
builder.Services.AddCascadingAuthenticationState();
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

        options.AddSecurityDefinition("cookie", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            In = Microsoft.OpenApi.Models.ParameterLocation.Cookie,
            Name = ".AspNetCore.Cookies",
            Description = "Cookie-based authentication"
        });

        options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "cookie"
                    }
                },
                Array.Empty<string>()
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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<RateLimitHeadersMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<RunEventsHub>("/hubs/runs").RequireAuthorization("viewer");
app.MapAuthEndpoints();
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
