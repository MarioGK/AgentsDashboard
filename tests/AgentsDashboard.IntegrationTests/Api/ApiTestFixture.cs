using System.Net.Http.Headers;
using System.Security.Claims;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace AgentsDashboard.IntegrationTests.Api;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder()
        .WithImage("mongo:8.0")
        .Build();

    public HttpClient Client { get; private set; } = null!;
    public WebApplicationFactory<AgentsDashboard.ControlPlane.Program> Factory { get; private set; } = null!;
    public IServiceProvider Services => Factory.Services;
    public string ConnectionString { get; private set; } = string.Empty;
    public string DatabaseName { get; } = $"test_api_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        ConnectionString = _mongoContainer.GetConnectionString();

        var controlPlaneAssembly = typeof(OrchestratorStore).Assembly;

        Factory = new WebApplicationFactory<AgentsDashboard.ControlPlane.Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var mongoClientDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMongoClient));
                if (mongoClientDescriptor != null)
                    services.Remove(mongoClientDescriptor);

                var storeInterfaceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IOrchestratorStore));
                if (storeInterfaceDescriptor != null)
                    services.Remove(storeInterfaceDescriptor);

                var storeDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(OrchestratorStore));
                if (storeDescriptor != null)
                    services.Remove(storeDescriptor);

                services.AddSingleton<IMongoClient>(_ => new MongoClient(ConnectionString));

                services.AddSingleton<OrchestratorStore>(sp =>
                {
                    var client = sp.GetRequiredService<IMongoClient>();
                    var options = Options.Create(new OrchestratorOptions
                    {
                        MongoConnectionString = ConnectionString,
                        MongoDatabase = DatabaseName,
                    });
                    return new OrchestratorStore(client, options);
                });

                services.AddSingleton<IOrchestratorStore>(sp => sp.GetRequiredService<OrchestratorStore>());

                var hostedServices = services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
                foreach (var service in hostedServices)
                {
                    services.Remove(service);
                }

                var dispatcherDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(RunDispatcher));
                if (dispatcherDescriptor != null)
                    services.Remove(dispatcherDescriptor);

                services.AddSingleton<RunDispatcher>(sp =>
                {
                    var store = sp.GetRequiredService<OrchestratorStore>();
                    var options = sp.GetRequiredService<IOptions<OrchestratorOptions>>();
                    var logger = sp.GetService<ILogger<RunDispatcher>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RunDispatcher>.Instance;
                    var publisher = sp.GetService<IRunEventPublisher>() ?? new NullRunEventPublisher();
                    return new RunDispatcher(
                        new MockWorkerClient(),
                        store,
                        sp.GetService<SecretCryptoService>()!,
                        publisher,
                        options,
                        logger);
                });

                var reaperDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IContainerReaper));
                if (reaperDescriptor != null)
                    services.Remove(reaperDescriptor);

                services.AddSingleton<IContainerReaper, MockContainerReaper>();

                RemoveAllAuthenticationServices(services);

                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Test")
                        .RequireAuthenticatedUser()
                        .Build();
                    options.AddPolicy("viewer", policy => policy.RequireAuthenticatedUser().AddAuthenticationSchemes("Test"));
                    options.AddPolicy("operator", policy => policy.RequireAuthenticatedUser().AddAuthenticationSchemes("Test"));
                });
            });

            builder.UseEnvironment("Testing");
        });

        Client = Factory.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _mongoContainer.DisposeAsync();
    }

    private static void RemoveAllAuthenticationServices(IServiceCollection services)
    {
        var authTypes = new[]
        {
            typeof(IAuthenticationService),
            typeof(IAuthenticationSchemeProvider),
            typeof(IAuthenticationHandlerProvider),
            typeof(IAuthenticationRequestHandler),
            typeof(Microsoft.AspNetCore.Authentication.IClaimsTransformation),
            typeof(Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Authentication.AuthenticationOptions>),
            typeof(Microsoft.Extensions.Options.IPostConfigureOptions<Microsoft.AspNetCore.Authentication.AuthenticationOptions>),
        };

        foreach (var type in authTypes)
        {
            var descriptors = services.Where(d => d.ServiceType == type).ToList();
            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }
        }

        var authHandlers = services.Where(d => 
            d.ServiceType.IsAssignableTo(typeof(IAuthenticationHandler)) ||
            (d.ImplementationType?.IsAssignableTo(typeof(IAuthenticationHandler)) ?? false)).ToList();
        foreach (var descriptor in authHandlers)
        {
            services.Remove(descriptor);
        }
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiTestFixture>;

public sealed class NullRunEventPublisher : IRunEventPublisher
{
    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MockWorkerClient : AgentsDashboard.Contracts.Worker.WorkerGateway.WorkerGatewayClient
{
    public override Grpc.Core.AsyncUnaryCall<DispatchJobReply> DispatchJobAsync(DispatchJobRequest request, Grpc.Core.Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var reply = new DispatchJobReply { Accepted = true };
        return new Grpc.Core.AsyncUnaryCall<DispatchJobReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }

    public override Grpc.Core.AsyncUnaryCall<CancelJobReply> CancelJobAsync(CancelJobRequest request, Grpc.Core.Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var reply = new CancelJobReply { Accepted = true };
        return new Grpc.Core.AsyncUnaryCall<CancelJobReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }

    public override Grpc.Core.AsyncUnaryCall<KillContainerReply> KillContainerAsync(KillContainerRequest request, Grpc.Core.Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var reply = new KillContainerReply { Killed = true, ContainerId = "mock-container-id" };
        return new Grpc.Core.AsyncUnaryCall<KillContainerReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }

    public override Grpc.Core.AsyncUnaryCall<ReconcileOrphanedContainersReply> ReconcileOrphanedContainersAsync(ReconcileOrphanedContainersRequest request, Grpc.Core.Metadata? headers = null, DateTime? deadline = null, CancellationToken cancellationToken = default)
    {
        var reply = new ReconcileOrphanedContainersReply { OrphanedCount = 0 };
        return new Grpc.Core.AsyncUnaryCall<ReconcileOrphanedContainersReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }
}

public sealed class MockContainerReaper : IContainerReaper
{
    public Task<ContainerKillResult> KillContainerAsync(string runId, string reason, bool force, CancellationToken cancellationToken)
    {
        return Task.FromResult(new ContainerKillResult { Killed = true, ContainerId = "mock-container-id" });
    }

    public Task<int> ReapOrphanedContainersAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken)
    {
        return Task.FromResult(0);
    }
}
