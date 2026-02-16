using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using MagicOnion.Server;
using MessagePack;
using MessagePack.Resolvers;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddOptionsWithValidateOnStart<WorkerOptions>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5201, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

StaticCompositeResolver.Instance.Register(
    MessagePack.Resolvers.GeneratedResolver.Instance,
    MessagePack.Resolvers.StandardResolver.Instance
);

builder.Services.AddMagicOnion(options =>
{
    options.MessagePackSerializerOptions = MessagePackSerializerOptions.Standard
        .WithResolver(StaticCompositeResolver.Instance);
});

builder.Services.AddSingleton<WorkerQueue>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorkerOptions>>().Value;
    return new WorkerQueue(options);
});
builder.Services.AddSingleton<WorkerEventBus>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton<HarnessAdapterFactory>();
builder.Services.AddSingleton<DockerContainerService>();
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
app.MapGet("/", () => "WorkerGateway gRPC endpoint is running.");
app.MapDefaultEndpoints();

app.Run();
