using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime;

/// <summary>
/// Receiver interface for broadcast commands FROM control plane TO task runtimes.
/// Task Runtimes implement this to receive broadcast commands.
/// </summary>
public interface ITaskRuntimeControlReceiver
{
    /// <summary>
    /// Called when control plane requests status from all task runtimes.
    /// </summary>
    void OnStatusRequest(StatusRequestMessage request);

    /// <summary>
    /// Called when configuration has changed and task runtimes should refresh.
    /// </summary>
    void OnConfigurationChanged(ConfigurationChangedMessage message);

    /// <summary>
    /// Called when control plane requests task runtimes to shut down.
    /// </summary>
    void OnShutdownRequest(ShutdownRequestMessage request);
}

/// <summary>
/// StreamingHub interface for task runtime control.
/// Task Runtimes register themselves; control plane broadcasts to all registered task runtimes.
/// </summary>
public interface ITaskRuntimeControlHub : IStreamingHub<ITaskRuntimeControlHub, ITaskRuntimeControlReceiver>
{
    /// <summary>
    /// Register this task runtime with the control plane.
    /// </summary>
    Task<TaskRuntimeRegistrationResult> RegisterAsync(TaskRuntimeRegistrationRequest request);

    /// <summary>
    /// Unregister this task runtime from the control plane.
    /// </summary>
    Task UnregisterAsync();

    /// <summary>
    /// Report task runtime status to control plane.
    /// </summary>
    Task ReportStatusAsync(TaskRuntimeStatusReport report);
}
