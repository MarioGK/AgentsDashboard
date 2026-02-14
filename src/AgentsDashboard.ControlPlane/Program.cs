using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Auth;
using AgentsDashboard.ControlPlane.Components;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Endpoints;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MudBlazor.Services;
using Yarp.ReverseProxy.Configuration;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection(OrchestratorOptions.SectionName));
builder.Services.Configure<DashboardAuthOptions>(builder.Configuration.GetSection(DashboardAuthOptions.SectionName));

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

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var options = sp.GetRequiredService<IOptions<OrchestratorOptions>>().Value;
    return new MongoClient(options.MongoConnectionString);
});

builder.Services.AddSingleton<OrchestratorStore>();
builder.Services.AddSingleton<RunDispatcher>();
builder.Services.AddSingleton<SecretCryptoService>();
builder.Services.AddSingleton<WebhookService>();
builder.Services.AddSingleton<IRunEventPublisher, SignalRRunEventPublisher>();
builder.Services.AddHostedService<MongoInitializationService>();
builder.Services.AddHostedService<RecoveryService>();
builder.Services.AddHostedService<CronSchedulerService>();
builder.Services.AddHostedService<WorkerEventListenerService>();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<AlertingService>();
builder.Services.AddSingleton<WorkflowExecutor>();
builder.Services.AddSingleton<ImageBuilderService>();
builder.Services.AddSingleton<CredentialValidationService>();
builder.Services.AddSingleton<TaskTemplateService>();
builder.Services.AddHostedService<TaskTemplateInitializationService>();
builder.Services.AddSingleton<IContainerReaper, ContainerReaper>();
builder.Services.AddHostedService<HarnessHealthService>();
builder.Services.AddTransient<ProxyAuditMiddleware>();
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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<RunEventsHub>("/hubs/runs").RequireAuthorization("viewer");
app.MapAuthEndpoints();
app.MapOrchestratorApi();
app.UseMiddleware<ProxyAuditMiddleware>();
app.MapReverseProxy();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();

app.Run();

namespace AgentsDashboard.ControlPlane
{
    public partial class Program { }
}
