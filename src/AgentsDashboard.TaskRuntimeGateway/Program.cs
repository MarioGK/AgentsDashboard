using System.Text.Json;
using AgentsDashboard.TaskRuntimeGateway;
using AgentsDashboard.TaskRuntimeGateway.Adapters;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.Services;
using AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;
using MagicOnion.Serialization.MessagePack;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;
using ZLogger;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredContainerLogging("TaskRuntimeGateway");

builder.Services.AddTaskRuntimeGatewayHealthChecks();
builder.Services.Configure<TaskRuntimeOptions>(builder.Configuration.GetSection(TaskRuntimeOptions.SectionName));
builder.Services.PostConfigure<TaskRuntimeOptions>(options =>
{
    options.ArtifactStoragePath = RepositoryPathResolver.ResolveDataPath(
        options.ArtifactStoragePath,
        TaskRuntimeOptions.DefaultArtifactStoragePath);
});
builder.Services.AddOptionsWithValidateOnStart<TaskRuntimeOptions>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TaskRuntimeOptions>>().Value);

var startupOptions = builder.Configuration.GetSection(TaskRuntimeOptions.SectionName).Get<TaskRuntimeOptions>();
if (startupOptions is not null)
{
    EnsureArtifactStorageDirectoryExists(startupOptions.ArtifactStoragePath);
}

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

builder.Services.AddSingleton<TaskRuntimeEventDispatcher>();

builder.Services.AddSingleton<ITaskRuntimeQueue>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TaskRuntimeOptions>>().Value;
    return new TaskRuntimeQueue(options);
});
builder.Services.AddSingleton<TaskRuntimeEventBus>();
builder.Services.AddHostedService<TaskRuntimeEventBroadcastService>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<HarnessAdapterFactory>();
builder.Services.AddSingleton<CodexAppServerRuntime>();
builder.Services.AddSingleton<OpenCodeSseRuntime>();
builder.Services.AddSingleton<IHarnessRuntimeFactory, DefaultHarnessRuntimeFactory>();
builder.Services.AddSingleton<TaskRuntimeHarnessToolHealthService>();
builder.Services.AddSingleton<DockerContainerService>();
builder.Services.AddSingleton<IDockerContainerService>(sp => sp.GetRequiredService<DockerContainerService>());
builder.Services.AddSingleton<IArtifactExtractor, ArtifactExtractor>();
builder.Services.AddSingleton<IHarnessExecutor, HarnessExecutor>();
builder.Services.AddSingleton<IContainerOrphanReconciler, ContainerOrphanReconciler>();
builder.Services.AddSingleton<JobProcessorService>();
builder.Services.AddSingleton<ImageBootstrapWorkScheduler>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobProcessorService>());
builder.Services.AddHostedService<TaskRuntimeHeartbeatService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ImageBootstrapWorkScheduler>());
builder.Services.AddHostedService<ImagePrePullService>();
builder.Services.AddSingleton<DockerHealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockerHealthCheckService>());

var app = builder.Build();

app.MapMagicOnionService();
app.MapGet("/", () => "TaskRuntimeGateway MagicOnion endpoint is running.");
app.MapTaskRuntimeGatewayHealthChecks();

app.Run();

static void EnsureArtifactStorageDirectoryExists(string? artifactStoragePath)
{
    if (string.IsNullOrWhiteSpace(artifactStoragePath))
    {
        return;
    }

    var resolvedPath = RepositoryPathResolver.ResolveDataPath(
        artifactStoragePath,
        TaskRuntimeOptions.DefaultArtifactStoragePath);

    if (resolvedPath == ":memory:")
    {
        return;
    }

    Directory.CreateDirectory(resolvedPath);
}
