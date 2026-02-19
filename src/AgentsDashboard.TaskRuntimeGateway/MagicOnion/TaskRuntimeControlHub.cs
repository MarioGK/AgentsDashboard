using AgentsDashboard.Contracts.TaskRuntime;
using MagicOnion.Server;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.TaskRuntimeGateway.MagicOnion;

/// <summary>
/// StreamingHub for task runtime control.
/// Task Runtimes connect to register themselves and receive broadcast commands from the control plane.
/// </summary>
public sealed class TaskRuntimeControlHub : StreamingHubBase<ITaskRuntimeControlHub, ITaskRuntimeControlReceiver>, ITaskRuntimeControlHub
{
    private readonly ILogger<TaskRuntimeControlHub> _logger;
    private string? _registeredTaskRuntimeId;

    public TaskRuntimeControlHub(ILogger<TaskRuntimeControlHub> logger)
    {
        _logger = logger;
    }

    protected override async ValueTask OnConnecting()
    {
        _logger.LogDebug("TaskRuntime connecting to control hub");
        await Task.CompletedTask;
    }

    protected override async ValueTask OnDisconnected()
    {
        if (!string.IsNullOrEmpty(_registeredTaskRuntimeId))
        {
            _logger.LogInformation("TaskRuntime {TaskRuntimeId} disconnected from control hub", _registeredTaskRuntimeId);
        }
        else
        {
            _logger.LogDebug("Unknown task runtime disconnected from control hub");
        }
        await Task.CompletedTask;
    }

    public async Task<TaskRuntimeRegistrationResult> RegisterAsync(TaskRuntimeRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TaskRuntimeId))
        {
            _logger.LogWarning("TaskRuntime registration rejected: missing TaskRuntimeId");
            return new TaskRuntimeRegistrationResult { Success = false, ErrorMessage = "TaskRuntimeId is required" };
        }

        _registeredTaskRuntimeId = request.TaskRuntimeId;

        _logger.LogInformation(
            "TaskRuntime {TaskRuntimeId} registered from endpoint {Endpoint} with {MaxSlots} slots. Capabilities: {Capabilities}",
            request.TaskRuntimeId,
            request.Endpoint ?? "unknown",
            request.MaxSlots,
            request.Capabilities != null ? string.Join(", ", request.Capabilities.Keys) : "none");

        await Task.CompletedTask;

        return new TaskRuntimeRegistrationResult { Success = true };
    }

    public async Task UnregisterAsync()
    {
        if (string.IsNullOrEmpty(_registeredTaskRuntimeId))
        {
            _logger.LogDebug("Unregister called but no task runtime was registered");
            return;
        }

        _logger.LogInformation("TaskRuntime {TaskRuntimeId} unregistered from control hub", _registeredTaskRuntimeId);
        _registeredTaskRuntimeId = null;

        await Task.CompletedTask;
    }

    public async Task ReportStatusAsync(TaskRuntimeStatusReport report)
    {
        if (string.IsNullOrEmpty(_registeredTaskRuntimeId))
        {
            _logger.LogWarning("Status report received from unregistered task runtime");
            return;
        }

        _logger.LogDebug(
            "Status report from task runtime {TaskRuntimeId}: {ActiveSlots}/{MaxSlots} slots used, CPU: {CpuUsage}%, Memory: {MemoryUsed}",
            report.TaskRuntimeId,
            report.ActiveSlots,
            report.MaxSlots,
            report.CpuUsagePercent,
            report.MemoryUsedBytes);

        await Task.CompletedTask;
    }
}
