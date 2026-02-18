using System.Text.Json;
using AgentsDashboard.WorkerGateway;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;
using Cysharp.Runtime.Multicast;
using Cysharp.Runtime.Multicast.InMemory;
using MagicOnion.Serialization.MessagePack;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ZLogger;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredContainerLogging("WorkerGateway");

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), ["live", "ready"])
    .AddCheck<DockerHealthCheckService>("docker", tags: ["ready"]);
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddOptionsWithValidateOnStart<WorkerOptions>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5201, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

var messagePackOptions = MessagePackSerializerOptions.Standard
    .WithResolver(StandardResolver.Instance);

builder.Services.AddMagicOnion(options =>
{
    options.MessageSerializer = MessagePackMagicOnionSerializerProvider.Default
        .WithOptions(messagePackOptions);
});
builder.Services.AddSingleton<IMulticastGroupProvider>(_ =>
    new InMemoryGroupProvider(DynamicInMemoryProxyFactory.Instance));

builder.Services.AddSingleton<IWorkerQueue>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value;
    return new WorkerQueue(options);
});
builder.Services.AddSingleton<WorkerEventBus>();
builder.Services.AddHostedService<WorkerEventBroadcastService>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<HarnessAdapterFactory>();
builder.Services.AddSingleton<CommandHarnessRuntime>();
builder.Services.AddSingleton<CodexAppServerRuntime>();
builder.Services.AddSingleton<OpenCodeSseRuntime>();
builder.Services.AddSingleton<ClaudeStreamRuntime>(sp => new ClaudeStreamRuntime(
    sp.GetRequiredService<SecretRedactor>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ClaudeStreamRuntime>()));
builder.Services.AddSingleton<ZaiClaudeCompatibleRuntime>();
builder.Services.AddSingleton<IHarnessRuntimeFactory, DefaultHarnessRuntimeFactory>();
builder.Services.AddSingleton<WorkerHarnessToolHealthService>();
builder.Services.AddSingleton<DockerContainerService>();
builder.Services.AddSingleton<IDockerContainerService>(sp => sp.GetRequiredService<DockerContainerService>());
builder.Services.AddSingleton<IArtifactExtractor, ArtifactExtractor>();
builder.Services.AddSingleton<IHarnessExecutor, HarnessExecutor>();
builder.Services.AddSingleton<IContainerOrphanReconciler, ContainerOrphanReconciler>();
builder.Services.AddSingleton<IJobProcessorService, JobProcessorService>();
builder.Services.AddSingleton<IImageBootstrapWorkScheduler, ImageBootstrapWorkScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IJobProcessorService>());
builder.Services.AddHostedService<WorkerHeartbeatService>();
builder.Services.AddHostedService(sp => (ImageBootstrapWorkScheduler)sp.GetRequiredService<IImageBootstrapWorkScheduler>());
builder.Services.AddHostedService<ImagePrePullService>();
builder.Services.AddSingleton<DockerHealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockerHealthCheckService>());

var app = builder.Build();

app.MapMagicOnionService();
app.MapGet("/", () => "WorkerGateway MagicOnion endpoint is running.");

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

    return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}
