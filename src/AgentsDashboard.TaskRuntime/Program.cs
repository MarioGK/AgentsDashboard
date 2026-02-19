using System.Text.Json;
using AgentsDashboard.TaskRuntime;
using AgentsDashboard.TaskRuntime.Adapters;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.Services;
using AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;
using MagicOnion.Serialization.MessagePack;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;
using ZLogger;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddStructuredContainerLogging("TaskRuntime");

builder.Services.AddTaskRuntimeHealthChecks();
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
builder.Services.AddSingleton<WorkspacePathGuard>();
builder.Services.AddSingleton<TaskRuntimeFileService>();
builder.Services.AddSingleton<TaskRuntimeGitService>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<HarnessAdapterFactory>();
builder.Services.AddSingleton<CodexAppServerRuntime>();
builder.Services.AddSingleton<OpenCodeSseRuntime>();
builder.Services.AddSingleton<IHarnessRuntimeFactory, DefaultHarnessRuntimeFactory>();
builder.Services.AddSingleton<TaskRuntimeHarnessToolHealthService>();
builder.Services.AddSingleton<IArtifactExtractor, ArtifactExtractor>();
builder.Services.AddSingleton<IHarnessExecutor, HarnessExecutor>();
builder.Services.AddSingleton<JobProcessorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobProcessorService>());
builder.Services.AddHostedService<TaskRuntimeHeartbeatService>();

var app = builder.Build();

app.MapMagicOnionService();
app.MapGet("/", () => "TaskRuntime MagicOnion endpoint is running.");
app.MapTaskRuntimeHealthChecks();

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
