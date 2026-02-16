using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Cysharp.Runtime.Multicast;
using Cysharp.Runtime.Multicast.InMemory;
using MagicOnion.Server;
using MagicOnion.Serialization.MessagePack;
using MessagePack;
using MessagePack.Resolvers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddOptionsWithValidateOnStart<WorkerOptions>();
builder.Services.Configure<TerminalOptions>(builder.Configuration.GetSection(TerminalOptions.SectionName));

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

builder.Services.AddSingleton<WorkerQueue>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value;
    return new WorkerQueue(options);
});
builder.Services.AddSingleton<WorkerEventBus>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<HarnessAdapterFactory>();
builder.Services.AddSingleton<DockerContainerService>();
builder.Services.AddSingleton<ITerminalSessionManager, TerminalSessionManager>();
builder.Services.AddSingleton<IArtifactExtractor, ArtifactExtractor>();
builder.Services.AddSingleton<IHarnessExecutor, HarnessExecutor>();
builder.Services.AddSingleton<IContainerOrphanReconciler, ContainerOrphanReconciler>();
builder.Services.AddSingleton<IJobProcessorService, JobProcessorService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IJobProcessorService>());
builder.Services.AddHostedService<WorkerHeartbeatService>();
builder.Services.AddHostedService<ImagePrePullService>();
builder.Services.AddHttpClient<WorkerHeartbeatService>();
builder.Services.AddSingleton<DockerHealthCheckService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DockerHealthCheckService>());
builder.Services.AddHealthChecks().AddCheck<DockerHealthCheckService>("docker", tags: ["live"]);

var app = builder.Build();

app.MapMagicOnionService();
app.MapGet("/", () => "WorkerGateway MagicOnion endpoint is running.");
app.MapDefaultEndpoints();

app.Run();
