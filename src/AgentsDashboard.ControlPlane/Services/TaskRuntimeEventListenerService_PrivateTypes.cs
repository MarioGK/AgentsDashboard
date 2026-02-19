using System.Collections.Concurrent;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class TaskRuntimeEventListenerService
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager,
    IOrchestratorStore store,
    ITaskSemanticEmbeddingService taskSemanticEmbeddingService,
    ITaskRuntimeRegistryService workerRegistry,
    IRunEventPublisher publisher,
    RunDispatcher dispatcher,
    ILogger<TaskRuntimeEventListenerService> logger,
    IRunStructuredViewService? runStructuredViewService = null) : BackgroundService, ITaskRuntimeEventReceiver
{
    private sealed class NullRunStructuredViewService : IRunStructuredViewService
    {
        public static readonly NullRunStructuredViewService Instance = new();

        public Task<RunStructuredProjectionDelta> ApplyStructuredEventAsync(
            RunStructuredEventDocument structuredEvent,
            CancellationToken cancellationToken)
        {
            var snapshot = new RunStructuredViewSnapshot(
                structuredEvent.RunId,
                structuredEvent.Sequence,
                [],
                [],
                [],
                null,
                structuredEvent.CreatedAtUtc == default ? DateTime.UtcNow : structuredEvent.CreatedAtUtc);
            return Task.FromResult(new RunStructuredProjectionDelta(snapshot, null, null));
        }

        public Task<RunStructuredViewSnapshot> GetViewAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RunStructuredViewSnapshot(runId, 0, [], [], [], null, DateTime.UtcNow));
        }
    }

    private sealed class TaskRuntimeHubConnection
    {
        public required string TaskRuntimeId { get; init; }
        public required string Endpoint { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public Task? ConnectionTask { get; set; }
        private ITaskRuntimeEventHub? _hub;
        private readonly SemaphoreSlim _hubLock = new(1, 1);

        public static TaskRuntimeHubConnection Create(string runtimeId, string endpoint)
        {
            return new TaskRuntimeHubConnection
            {
                TaskRuntimeId = runtimeId,
                Endpoint = endpoint,
                Cancellation = new CancellationTokenSource(),
            };
        }

        public void SetHub(ITaskRuntimeEventHub hub)
        {
            _hub = hub;
        }

        public async Task DisposeHubAsync()
        {
            await _hubLock.WaitAsync();
            try
            {
                if (_hub is null)
                {
                    return;
                }

                await _hub.DisposeAsync();
                _hub = null;
            }
            catch
            {
            }
            finally
            {
                _hubLock.Release();
            }
        }
    }
}
