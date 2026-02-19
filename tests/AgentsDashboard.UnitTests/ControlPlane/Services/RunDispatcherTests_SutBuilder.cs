using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class RunDispatcherTests
{
        private sealed class SutBuilder
        {
            public Mock<IOrchestratorStore> Store { get; } = new();
            public Mock<ITaskRuntimeLifecycleManager> WorkerLifecycleManagerMock { get; } = new();
            public Mock<ISecretCryptoService> SecretCryptoMock { get; } = new();
            public Mock<IRunEventPublisher> PublisherMock { get; } = new();
            public Mock<IMagicOnionClientFactory> ClientFactoryMock { get; } = new();
            public Mock<ITaskRuntimeGatewayService> WorkerClientMock { get; } = new();
            public RunDispatcher Dispatcher { get; }

            private readonly OrchestratorOptions _options = new();

            public SutBuilder()
            {
                WorkerLifecycleManagerMock.Setup(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(
                    new TaskRuntimeLease("worker-1", "container-1", "http://worker.local:5201"));
                WorkerLifecycleManagerMock.Setup(x => x.RecordDispatchActivityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                ClientFactoryMock
                    .Setup(f => f.CreateTaskRuntimeGatewayService(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns(WorkerClientMock.Object);
                Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
                Store.Setup(s => s.CountActiveRunsByRepoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
                Store.Setup(s => s.CountActiveRunsByTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
                Store.Setup(s => s.ListRunsByTaskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
                Store.Setup(s => s.ListProviderSecretsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
                Store.Setup(s => s.GetHarnessProviderSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((HarnessProviderSettingsDocument?)null);
                Store.Setup(s => s.GetInstructionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
                Store.Setup(s => s.UpdateTaskGitMetadataAsync(
                        It.IsAny<string>(),
                        It.IsAny<DateTime?>(),
                        It.IsAny<string?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync((TaskDocument?)null);
                Store.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new SystemSettingsDocument { Orchestrator = new OrchestratorSettings() });
                Store.Setup(s => s.MarkRunCompletedAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
                    .ReturnsAsync((string runId, bool _, string _, string _, CancellationToken _, string? _, string? _) =>
                    new RunDocument { Id = runId, State = RunState.Failed });
                Store.Setup(s => s.MarkRunStartedAsync(
                        It.IsAny<string>(),
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>(),
                        It.IsAny<string?>(),
                        It.IsAny<string?>(),
                        It.IsAny<string?>()))
                    .ReturnsAsync((string runId, string _, CancellationToken _, string? _, string? _, string? _) =>
                        new RunDocument { Id = runId, State = RunState.Running });
                Store.Setup(s => s.CreateFindingFromFailureAsync(It.IsAny<RunDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new FindingDocument());
                Store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
                PublisherMock.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
                Dispatcher = new RunDispatcher(
                    ClientFactoryMock.Object,
                    Store.Object,
                    WorkerLifecycleManagerMock.Object,
                    SecretCryptoMock.Object,
                    PublisherMock.Object,
                    Options.Create(_options),
                    NullLogger<RunDispatcher>.Instance);
            }

            public SutBuilder WithActiveWorker()
            {
                WorkerLifecycleManagerMock
                    .Setup(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new TaskRuntimeLease("worker-1", "container-1", "http://worker.local:5201"));
                return this;
            }

            public SutBuilder Build()
            {
                return this;
            }
        }

}
