using AgentsDashboard.ControlPlane;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
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
    startupOptions.LiteDbPath.EnsureLiteDbDirectoryExists();
    startupOptions.ArtifactsRootPath.EnsureArtifactsDirectoryExists();
}

builder.Services.AddControlPlaneServices(builder.Environment.IsDevelopment());

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
app.UseAntiforgery();

app.MapStaticAssets();

app.MapMagicOnionService();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var readyHealthCheckOptions = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
}
.WithJsonResponseWriter();
var liveHealthCheckOptions = new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
}
.WithJsonResponseWriter();

app.MapHealthChecks("/health", readyHealthCheckOptions);
app.MapHealthChecks("/ready", readyHealthCheckOptions);
app.MapHealthChecks("/alive", liveHealthCheckOptions);

app.Run();
