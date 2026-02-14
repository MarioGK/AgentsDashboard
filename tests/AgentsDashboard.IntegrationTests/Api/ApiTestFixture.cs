using System.Net;
using System.Net.Http.Headers;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.IntegrationTests.Api;

public sealed class ApiTestFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agentsdashboard-api-{Guid.NewGuid():N}.db");

    public HttpClient Client { get; private set; } = null!;
    public WebApplicationFactory<AgentsDashboard.ControlPlane.Program> Factory { get; private set; } = null!;
    public IServiceProvider Services => Factory.Services;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = $"Data Source={_databasePath}";

        var dbOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        await using (var dbContext = new OrchestratorDbContext(dbOptions))
        {
            await dbContext.Database.MigrateAsync();
        }

        Environment.SetEnvironmentVariable("Orchestrator__SqliteConnectionString", ConnectionString);

        Factory = new WebApplicationFactory<AgentsDashboard.ControlPlane.Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
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
                    var yarpProvider = sp.GetService<AgentsDashboard.ControlPlane.Proxy.InMemoryYarpConfigProvider>() ?? new AgentsDashboard.ControlPlane.Proxy.InMemoryYarpConfigProvider();
                    var lifecycle = sp.GetService<IWorkerLifecycleManager>() ?? new MockWorkerLifecycleManager();
                    return new RunDispatcher(
                        new MockWorkerClient(),
                        store,
                        lifecycle,
                        sp.GetService<ISecretCryptoService>()!,
                        publisher,
                        yarpProvider,
                        options,
                        logger);
                });

                var reaperDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IContainerReaper));
                if (reaperDescriptor != null)
                    services.Remove(reaperDescriptor);

                services.AddSingleton<IContainerReaper, MockContainerReaper>();

                var cryptoDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(SecretCryptoService));
                if (cryptoDescriptor != null)
                    services.Remove(cryptoDescriptor);

                var cryptoInterfaceDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ISecretCryptoService));
                if (cryptoInterfaceDescriptor != null)
                    services.Remove(cryptoInterfaceDescriptor);

                services.AddSingleton<SecretCryptoService>(_ => new MockSecretCryptoService());
                services.AddSingleton<ISecretCryptoService>(sp => sp.GetRequiredService<SecretCryptoService>());
            });

            builder.UseEnvironment("Testing");
        });

        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        Client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        Environment.SetEnvironmentVariable("Orchestrator__SqliteConnectionString", null);
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiTestFixture>;

public sealed class NullRunEventPublisher : IRunEventPublisher
{
    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishWorkerHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishRouteAvailableAsync(string runId, string routePath, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishWorkflowV2ExecutionStateAsync(WorkflowExecutionV2Document execution, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task PublishWorkflowV2NodeStateAsync(WorkflowExecutionV2Document execution, WorkflowNodeResult nodeResult, CancellationToken cancellationToken) => Task.CompletedTask;
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

public sealed class MockWorkerLifecycleManager : IWorkerLifecycleManager
{
    public Task<bool> EnsureWorkerRunningAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RecordDispatchActivityAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopWorkerIfIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class MockSecretCryptoService : SecretCryptoService
{
    public MockSecretCryptoService() : base(new MockDataProtectionProvider()) { }

    public new string Encrypt(string plaintext)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public new string Decrypt(string ciphertext)
        => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
}

public sealed class MockDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose)
        => new MockDataProtector();
}

public sealed class MockDataProtector : IDataProtector
{
    public IDataProtector CreateProtector(string purpose) => this;

    public byte[] Protect(byte[] plaintext)
        => plaintext;

    public byte[] Unprotect(byte[] protectedData)
        => protectedData;
}
