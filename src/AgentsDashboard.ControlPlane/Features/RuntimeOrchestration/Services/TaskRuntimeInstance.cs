

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed record TaskRuntimeInstance(
    string TaskRuntimeId,
    string TaskId,
    string ContainerId,
    string ContainerName,
    bool IsRunning,
    TaskRuntimeLifecycleState LifecycleState,
    bool IsDraining,
    string GrpcEndpoint,
    string ProxyEndpoint,
    int ActiveSlots,
    int MaxSlots,
    double CpuPercent,
    double MemoryPercent,
    DateTime LastActivityUtc,
    DateTime StartedAtUtc,
    int DispatchCount,
    string ImageRef,
    string ImageDigest,
    string ImageSource);
