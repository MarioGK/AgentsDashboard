using AgentsDashboard.Contracts.Worker;
using MagicOnion.Server;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.MagicOnion;

/// <summary>
/// StreamingHub for worker control via multicaster.
/// Workers connect to register themselves and receive broadcast commands from the control plane.
/// </summary>
public sealed class WorkerControlHub : StreamingHubBase<IWorkerControlHub, IWorkerControlReceiver>, IWorkerControlHub
{
    private readonly ILogger<WorkerControlHub> _logger;
    private IWorkerControlReceiver? _receiver;
    private string? _registeredWorkerId;

    public WorkerControlHub(ILogger<WorkerControlHub> logger)
    {
        _logger = logger;
    }

    protected override async ValueTask OnConnecting()
    {
        _receiver = Receiver;
        _logger.LogDebug("Worker connecting to control hub");
        await Task.CompletedTask;
    }

    protected override async ValueTask OnDisconnected()
    {
        if (!string.IsNullOrEmpty(_registeredWorkerId))
        {
            _logger.LogInformation("Worker {WorkerId} disconnected from control hub", _registeredWorkerId);
        }
        else
        {
            _logger.LogDebug("Unknown worker disconnected from control hub");
        }
        await Task.CompletedTask;
    }

    public async Task<WorkerRegistrationResult> RegisterAsync(WorkerRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.WorkerId))
        {
            _logger.LogWarning("Worker registration rejected: missing WorkerId");
            return new WorkerRegistrationResult { Success = false, ErrorMessage = "WorkerId is required" };
        }

        _registeredWorkerId = request.WorkerId;

        _logger.LogInformation(
            "Worker {WorkerId} registered from endpoint {Endpoint} with {MaxSlots} slots. Capabilities: {Capabilities}",
            request.WorkerId,
            request.Endpoint ?? "unknown",
            request.MaxSlots,
            request.Capabilities != null ? string.Join(", ", request.Capabilities.Keys) : "none");

        await Task.CompletedTask;

        return new WorkerRegistrationResult { Success = true };
    }

    public async Task UnregisterAsync()
    {
        if (string.IsNullOrEmpty(_registeredWorkerId))
        {
            _logger.LogDebug("Unregister called but no worker was registered");
            return;
        }

        _logger.LogInformation("Worker {WorkerId} unregistered from control hub", _registeredWorkerId);
        _registeredWorkerId = null;

        await Task.CompletedTask;
    }

    public async Task ReportStatusAsync(WorkerStatusReport report)
    {
        if (string.IsNullOrEmpty(_registeredWorkerId))
        {
            _logger.LogWarning("Status report received from unregistered worker");
            return;
        }

        _logger.LogDebug(
            "Status report from worker {WorkerId}: {ActiveSlots}/{MaxSlots} slots used, CPU: {CpuUsage}%, Memory: {MemoryUsed}",
            report.WorkerId,
            report.ActiveSlots,
            report.MaxSlots,
            report.CpuUsagePercent,
            report.MemoryUsedBytes);

        await Task.CompletedTask;
    }
}
