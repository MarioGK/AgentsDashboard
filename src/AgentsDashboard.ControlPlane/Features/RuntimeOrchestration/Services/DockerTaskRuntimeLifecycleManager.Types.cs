namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed partial class DockerTaskRuntimeLifecycleManager
{
    private sealed class ConcurrencySlot(DockerTaskRuntimeLifecycleManager parent, bool isBuild) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            parent.ReleaseConcurrencySlot(isBuild);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TaskRuntimeStateEntry
    {
        public required string TaskRuntimeId { get; init; }
        public required string TaskId { get; set; }
        public required string ContainerId { get; set; }
        public required string ContainerName { get; set; }
        public required bool IsRunning { get; set; }
        public required TaskRuntimeLifecycleState LifecycleState { get; set; }
        public required bool IsDraining { get; set; }
        public required string GrpcEndpoint { get; set; }
        public required string ProxyEndpoint { get; set; }
        public required int ActiveSlots { get; set; }
        public required int MaxSlots { get; set; }
        public required double CpuPercent { get; set; }
        public required double MemoryPercent { get; set; }
        public required DateTime LastActivityUtc { get; set; }
        public required DateTime StartedAtUtc { get; set; }
        public required DateTime DrainingSinceUtc { get; set; }
        public required DateTime LastPressureSampleUtc { get; set; }
        public required int DispatchCount { get; set; }
        public required string ImageRef { get; set; }
        public required string ImageDigest { get; set; }
        public required string ImageSource { get; set; }

        public static TaskRuntimeStateEntry Create(
            string workerId,
            string taskId,
            string containerId,
            string containerName,
            string grpcEndpoint,
            string proxyEndpoint,
            bool isRunning,
            int slotsPerWorker)
        {
            return new TaskRuntimeStateEntry
            {
                TaskRuntimeId = workerId,
                TaskId = taskId,
                ContainerId = containerId,
                ContainerName = containerName,
                IsRunning = isRunning,
                LifecycleState = isRunning ? TaskRuntimeLifecycleState.Ready : TaskRuntimeLifecycleState.Stopped,
                IsDraining = false,
                GrpcEndpoint = grpcEndpoint,
                ProxyEndpoint = proxyEndpoint,
                ActiveSlots = 0,
                MaxSlots = slotsPerWorker,
                CpuPercent = 0,
                MemoryPercent = 0,
                LastActivityUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow,
                DrainingSinceUtc = DateTime.MinValue,
                LastPressureSampleUtc = DateTime.MinValue,
                DispatchCount = 0,
                ImageRef = string.Empty,
                ImageDigest = string.Empty,
                ImageSource = string.Empty
            };
        }

        public TaskRuntimeInstance ToRuntime()
            => new(
                TaskRuntimeId,
                TaskId,
                ContainerId,
                ContainerName,
                IsRunning,
                LifecycleState,
                IsDraining,
                GrpcEndpoint,
                ProxyEndpoint,
                ActiveSlots,
                MaxSlots,
                CpuPercent,
                MemoryPercent,
                LastActivityUtc,
                StartedAtUtc,
                DispatchCount,
                ImageRef,
                ImageDigest,
                ImageSource);
    }
}
