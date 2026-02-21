using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Services;
using Grpc.Core;
using MagicOnion;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class TaskRuntimeFileSystemGatewayTests
{
    [Test]
    public async Task FileOperations_PassThroughToRuntimeClient()
    {
        var runtimeClient = new FakeTaskRuntimeService();
        var clientFactory = new FakeMagicOnionClientFactory(runtimeClient);
        var lifecycleManager = new FakeTaskRuntimeLifecycleManager(CreateRuntimeInstance("runtime-1", "http://127.0.0.1:5201"));
        var gateway = new TaskRuntimeFileSystemGateway(clientFactory, lifecycleManager);

        var listRequest = new ListRuntimeFilesRequest
        {
            RepositoryId = "repo-1",
            TaskId = "task-1",
            RelativePath = ".",
            IncludeHidden = false,
        };
        var createRequest = new CreateRuntimeFileRequest
        {
            RepositoryId = "repo-1",
            TaskId = "task-1",
            RelativePath = "output.txt",
            Content = "ok"u8.ToArray(),
            CreateParentDirectories = true,
            Overwrite = false,
        };
        var readRequest = new ReadRuntimeFileRequest
        {
            RepositoryId = "repo-1",
            TaskId = "task-1",
            RelativePath = "output.txt",
            MaxBytes = 0,
        };
        var deleteRequest = new DeleteRuntimeFileRequest
        {
            RepositoryId = "repo-1",
            TaskId = "task-1",
            RelativePath = "output.txt",
            Recursive = false,
        };

        var listResult = await gateway.ListRuntimeFilesAsync("runtime-1", listRequest, CancellationToken.None);
        var createResult = await gateway.CreateRuntimeFileAsync("runtime-1", createRequest, CancellationToken.None);
        var readResult = await gateway.ReadRuntimeFileAsync("runtime-1", readRequest, CancellationToken.None);
        var deleteResult = await gateway.DeleteRuntimeFileAsync("runtime-1", deleteRequest, CancellationToken.None);

        await Assert.That(listResult.Success).IsTrue();
        await Assert.That(createResult.Success).IsTrue();
        await Assert.That(readResult.Found).IsTrue();
        await Assert.That(deleteResult.Success).IsTrue();

        await Assert.That(runtimeClient.LastListRequest is not null).IsTrue();
        await Assert.That(runtimeClient.LastCreateRequest is not null).IsTrue();
        await Assert.That(runtimeClient.LastReadRequest is not null).IsTrue();
        await Assert.That(runtimeClient.LastDeleteRequest is not null).IsTrue();

        await Assert.That(clientFactory.LastRuntimeId).IsEqualTo("runtime-1");
        await Assert.That(clientFactory.LastGrpcAddress).IsEqualTo("http://127.0.0.1:5201");
    }

    [Test]
    public async Task RuntimeUnavailable_ThrowsInvalidOperationException()
    {
        var gateway = new TaskRuntimeFileSystemGateway(
            new FakeMagicOnionClientFactory(new FakeTaskRuntimeService()),
            new FakeTaskRuntimeLifecycleManager(null));

        Func<Task> action = async () =>
        {
            await gateway.ListRuntimeFilesAsync(
                "runtime-missing",
                new ListRuntimeFilesRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = ".",
                    IncludeHidden = false,
                },
                CancellationToken.None);
        };

        await Assert.That(action).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task InvalidRuntimeId_ThrowsArgumentException()
    {
        var gateway = new TaskRuntimeFileSystemGateway(
            new FakeMagicOnionClientFactory(new FakeTaskRuntimeService()),
            new FakeTaskRuntimeLifecycleManager(CreateRuntimeInstance("runtime-1", "http://127.0.0.1:5201")));

        Func<Task> action = async () =>
        {
            await gateway.ListRuntimeFilesAsync(
                " ",
                new ListRuntimeFilesRequest
                {
                    RepositoryId = "repo-1",
                    TaskId = "task-1",
                    RelativePath = ".",
                    IncludeHidden = false,
                },
                CancellationToken.None);
        };

        await Assert.That(action).Throws<ArgumentException>();
    }

    private static TaskRuntimeInstance CreateRuntimeInstance(string runtimeId, string grpcEndpoint)
    {
        return new TaskRuntimeInstance(
            runtimeId,
            "task-1",
            "container-1",
            "container-name",
            true,
            TaskRuntimeLifecycleState.Ready,
            false,
            grpcEndpoint,
            string.Empty,
            0,
            1,
            0,
            0,
            DateTime.UtcNow,
            DateTime.UtcNow,
            0,
            "image-ref",
            "image-digest",
            "image-source");
    }

    private sealed class FakeTaskRuntimeService : ITaskRuntimeService
    {
        public ListRuntimeFilesRequest? LastListRequest { get; private set; }
        public CreateRuntimeFileRequest? LastCreateRequest { get; private set; }
        public ReadRuntimeFileRequest? LastReadRequest { get; private set; }
        public DeleteRuntimeFileRequest? LastDeleteRequest { get; private set; }

        public UnaryResult<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request)
        {
            LastListRequest = request;
            return UnaryResult.FromResult(new ListRuntimeFilesResult
            {
                Success = true,
                Found = true,
                IsDirectory = true,
                Reason = null,
                ResolvedRelativePath = ".",
                Entries = [],
            });
        }

        public UnaryResult<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request)
        {
            LastCreateRequest = request;
            return UnaryResult.FromResult(new CreateRuntimeFileResult
            {
                Success = true,
                Created = true,
                Reason = null,
                RelativePath = request.RelativePath,
                ContentLength = request.Content?.LongLength ?? 0,
            });
        }

        public UnaryResult<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request)
        {
            LastReadRequest = request;
            return UnaryResult.FromResult(new ReadRuntimeFileResult
            {
                Found = true,
                IsDirectory = false,
                Truncated = false,
                ContentLength = 2,
                Content = "ok"u8.ToArray(),
                ContentType = "text/plain",
                Reason = null,
                RelativePath = request.RelativePath,
            });
        }

        public UnaryResult<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request)
        {
            LastDeleteRequest = request;
            return UnaryResult.FromResult(new DeleteRuntimeFileResult
            {
                Success = true,
                Deleted = true,
                WasDirectory = false,
                Reason = null,
                RelativePath = request.RelativePath,
            });
        }

        public UnaryResult<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request)
        {
            return UnaryResult.FromResult(new DispatchJobResult
            {
                Success = true,
                ErrorMessage = null,
                DispatchedAt = DateTimeOffset.UtcNow,
            });
        }

        public UnaryResult<StopJobResult> StopJobAsync(StopJobRequest request)
        {
            return UnaryResult.FromResult(new StopJobResult
            {
                Success = true,
                ErrorMessage = null,
            });
        }

        public UnaryResult<HealthCheckResult> CheckHealthAsync()
        {
            return UnaryResult.FromResult(new HealthCheckResult
            {
                Success = true,
                ErrorMessage = null,
                CheckedAt = DateTimeOffset.UtcNow,
            });
        }

        public UnaryResult<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request)
        {
            return UnaryResult.FromResult(new StartRuntimeCommandResult
            {
                Success = true,
                ErrorMessage = null,
                CommandId = "cmd-1",
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }

        public UnaryResult<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request)
        {
            return UnaryResult.FromResult(new CancelRuntimeCommandResult
            {
                Success = true,
                ErrorMessage = null,
                CanceledAt = DateTimeOffset.UtcNow,
            });
        }

        public UnaryResult<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request)
        {
            return UnaryResult.FromResult(new RuntimeCommandStatusResult
            {
                Found = true,
                CommandId = request.CommandId,
                Status = RuntimeCommandStatusValue.Completed,
            });
        }

        public ITaskRuntimeService WithOptions(CallOptions callOptions)
        {
            return this;
        }

        public ITaskRuntimeService WithHeaders(Metadata headers)
        {
            return this;
        }

        public ITaskRuntimeService WithDeadline(DateTime deadline)
        {
            return this;
        }

        public ITaskRuntimeService WithCancellationToken(CancellationToken cancellationToken)
        {
            return this;
        }

        public ITaskRuntimeService WithHost(string host)
        {
            return this;
        }
    }

    private sealed class FakeMagicOnionClientFactory(FakeTaskRuntimeService runtimeService) : IMagicOnionClientFactory
    {
        public string? LastRuntimeId { get; private set; }
        public string? LastGrpcAddress { get; private set; }

        public ITaskRuntimeService CreateTaskRuntimeService(string runtimeId, string grpcAddress)
        {
            LastRuntimeId = runtimeId;
            LastGrpcAddress = grpcAddress;
            return runtimeService;
        }

        public Task<ITaskRuntimeEventHub> ConnectEventHubAsync(string runtimeId, string grpcAddress, ITaskRuntimeEventReceiver receiver, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public void RemoveTaskRuntime(string runtimeId)
        {
        }
    }

    private sealed class FakeTaskRuntimeLifecycleManager(TaskRuntimeInstance? runtimeInstance) : ITaskRuntimeLifecycleManager
    {
        public Task EnsureTaskRuntimeImageAvailableAsync(CancellationToken cancellationToken, IProgress<BackgroundWorkSnapshot>? progress = null)
        {
            throw new NotSupportedException();
        }

        public Task EnsureMinimumTaskRuntimesAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TaskRuntimeLease?> AcquireTaskRuntimeForDispatchAsync(string repositoryId, string taskId, int requestedParallelSlots, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TaskRuntimeInstance?> GetTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken)
        {
            return Task.FromResult(runtimeInstance);
        }

        public Task<IReadOnlyList<TaskRuntimeInstance>> ListTaskRuntimesAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ReportTaskRuntimeHeartbeatAsync(string runtimeId, int activeSlots, int maxSlots, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RecordDispatchActivityAsync(string runtimeId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ScaleDownIdleTaskRuntimesAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SetTaskRuntimeDrainingAsync(string runtimeId, bool draining, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RecycleTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RecycleTaskRuntimePoolAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task RunReconciliationAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SetScaleOutPausedAsync(bool paused, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task ClearScaleOutCooldownAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<OrchestratorHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
