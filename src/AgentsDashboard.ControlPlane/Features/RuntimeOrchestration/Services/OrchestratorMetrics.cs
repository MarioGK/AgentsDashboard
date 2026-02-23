using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public interface IOrchestratorMetrics
{
    void RecordRunStart(string harness, string repositoryId);
    void RecordRunComplete(string harness, string state, double durationSeconds, string repositoryId);
    void RecordJobDispatch(string harness);
    void RecordError(string errorType, string errorCategory);
    void SetPendingJobs(int count);
    void SetActiveJobs(int count);
    void SetQueuedRuns(int count);
    void SetActiveRuns(int count);
    void RecordQueueWaitTime(double seconds);
    void SetWorkerSlots(string host, int activeSlots, int maxSlots);
    void RecordStatusUpdateLatency(double seconds);
    void RecordAlertFired(string ruleId);
    void RecordWebhookDelivery(string status);
    void SetSignalRConnections(int count);
    void RecordGrpcDuration(string method, double seconds);
    void RecordFinding(string severity);
    void RecordArtifactGenerated(string harness);
    void RecordContainerMetrics(string harness, double cpuPercent, double memoryBytes);
}

public class OrchestratorMetrics : IOrchestratorMetrics
{
    private static readonly Meter s_meter = new("AgentsDashboard.Orchestrator", "1.0.0");

    private readonly Counter<long> _runsTotal;
    private readonly Counter<long> _jobsTotal;
    private readonly Counter<long> _errorsTotal;
    private readonly Counter<long> _alertsFiredTotal;
    private readonly Counter<long> _webhookDeliveriesTotal;
    private readonly Counter<long> _findingsTotal;
    private readonly Counter<long> _artifactsTotal;

    private readonly UpDownCounter<int> _pendingJobs;
    private readonly UpDownCounter<int> _activeJobs;
    private readonly UpDownCounter<int> _queuedRuns;
    private readonly UpDownCounter<int> _activeRuns;
    private readonly UpDownCounter<int> _signalRConnections;

    private readonly Histogram<double> _runDuration;
    private readonly Histogram<double> _queueWaitTime;
    private readonly Histogram<double> _statusUpdateDuration;
    private readonly Histogram<double> _grpcDuration;

    private readonly ObservableGauge<int> _workerActiveSlots;
    private readonly ObservableGauge<int> _workerMaxSlots;

    private readonly Dictionary<string, (int Active, int Max)> _workerSlots = new();
    private readonly ConcurrentDictionary<string, double> _containerCpuByHarness = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, double> _containerMemoryByHarness = new(StringComparer.OrdinalIgnoreCase);
    private int _currentSignalRConnections;
    private int _currentPendingJobs;
    private int _currentActiveJobs;
    private int _currentQueuedRuns;
    private int _currentActiveRuns;

    public OrchestratorMetrics()
    {
        _runsTotal = s_meter.CreateCounter<long>("orchestrator_runs_total", "runs", "Total number of runs executed");
        _jobsTotal = s_meter.CreateCounter<long>("orchestrator_jobs_total", "jobs", "Total number of jobs dispatched");
        _errorsTotal = s_meter.CreateCounter<long>("orchestrator_errors_total", "errors", "Total number of errors");
        _alertsFiredTotal = s_meter.CreateCounter<long>("orchestrator_alerts_fired_total", "alerts", "Total alerts fired");
        _webhookDeliveriesTotal = s_meter.CreateCounter<long>("orchestrator_webhook_deliveries_total", "deliveries", "Total webhook deliveries");
        _findingsTotal = s_meter.CreateCounter<long>("orchestrator_findings_total", "findings", "Total findings created");
        _artifactsTotal = s_meter.CreateCounter<long>("orchestrator_artifacts_total", "artifacts", "Total artifacts generated");

        _pendingJobs = s_meter.CreateUpDownCounter<int>("orchestrator_pending_jobs", "jobs", "Current number of pending jobs");
        _activeJobs = s_meter.CreateUpDownCounter<int>("orchestrator_active_jobs", "jobs", "Current number of active jobs");
        _queuedRuns = s_meter.CreateUpDownCounter<int>("orchestrator_queued_runs", "runs", "Current number of queued runs");
        _activeRuns = s_meter.CreateUpDownCounter<int>("orchestrator_active_runs", "runs", "Current number of active runs");
        _signalRConnections = s_meter.CreateUpDownCounter<int>("orchestrator_signalr_connections_active", "connections", "Active SignalR connections");

        _runDuration = s_meter.CreateHistogram<double>("orchestrator_run_duration_seconds", "s", "Run execution duration");
        _queueWaitTime = s_meter.CreateHistogram<double>("orchestrator_queue_wait_seconds", "s", "Time jobs spend in queue");
        _statusUpdateDuration = s_meter.CreateHistogram<double>("orchestrator_status_update_duration_seconds", "s", "Status update processing duration");
        _grpcDuration = s_meter.CreateHistogram<double>("orchestrator_grpc_duration_seconds", "s", "gRPC call duration");

        _workerActiveSlots = s_meter.CreateObservableGauge<int>("orchestrator_worker_active_slots", () => GetWorkerSlots("active"), "slots", "Active worker slots");
        _workerMaxSlots = s_meter.CreateObservableGauge<int>("orchestrator_worker_max_slots", () => GetWorkerSlots("max"), "slots", "Maximum worker slots");
        _ = s_meter.CreateObservableGauge<double>("orchestrator_container_cpu_percent", () => GetContainerCpuMeasurements(), "percent", "Container CPU usage");
        _ = s_meter.CreateObservableGauge<double>("orchestrator_container_memory_bytes", () => GetContainerMemoryMeasurements(), "bytes", "Container memory usage");
    }

    private IEnumerable<Measurement<int>> GetWorkerSlots(string type)
    {
        lock (_workerSlots)
        {
            foreach (var (host, slots) in _workerSlots)
            {
                var value = type == "active" ? slots.Active : slots.Max;
                yield return new Measurement<int>(value, new KeyValuePair<string, object?>("host", host));
            }
        }
    }

    public void RecordRunStart(string harness, string repositoryId)
    {
        var tags = new TagList
        {
            { "harness", harness },
            { "repository_id", repositoryId }
        };
        _runsTotal.Add(1, tags);
    }

    public void RecordRunComplete(string harness, string state, double durationSeconds, string repositoryId)
    {
        var tags = new TagList
        {
            { "harness", harness },
            { "state", state },
            { "repository_id", repositoryId }
        };
        _runDuration.Record(durationSeconds, tags);
    }

    public void RecordJobDispatch(string harness)
    {
        _jobsTotal.Add(1, new KeyValuePair<string, object?>("harness", harness));
    }

    public void RecordError(string errorType, string errorCategory)
    {
        var tags = new TagList
        {
            { "error_type", errorType },
            { "error_category", errorCategory }
        };
        _errorsTotal.Add(1, tags);
    }

    public void SetPendingJobs(int count)
    {
        UpdateCounter(_pendingJobs, ref _currentPendingJobs, count);
    }

    public void SetActiveJobs(int count)
    {
        UpdateCounter(_activeJobs, ref _currentActiveJobs, count);
    }

    public void SetQueuedRuns(int count)
    {
        UpdateCounter(_queuedRuns, ref _currentQueuedRuns, count);
    }

    public void SetActiveRuns(int count)
    {
        UpdateCounter(_activeRuns, ref _currentActiveRuns, count);
    }

    public void RecordQueueWaitTime(double seconds)
    {
        _queueWaitTime.Record(seconds);
    }

    public void SetWorkerSlots(string host, int activeSlots, int maxSlots)
    {
        lock (_workerSlots)
        {
            _workerSlots[host] = (activeSlots, maxSlots);
        }
    }

    public void RecordStatusUpdateLatency(double seconds)
    {
        _statusUpdateDuration.Record(seconds);
    }

    public void RecordAlertFired(string ruleId)
    {
        _alertsFiredTotal.Add(1, new KeyValuePair<string, object?>("rule_id", ruleId));
    }

    public void RecordWebhookDelivery(string status)
    {
        _webhookDeliveriesTotal.Add(1, new KeyValuePair<string, object?>("status", status));
    }

    public void SetSignalRConnections(int count)
    {
        UpdateCounter(_signalRConnections, ref _currentSignalRConnections, count);
    }

    public void RecordGrpcDuration(string method, double seconds)
    {
        _grpcDuration.Record(seconds, new KeyValuePair<string, object?>("method", method));
    }

    public void RecordFinding(string severity)
    {
        _findingsTotal.Add(1, new KeyValuePair<string, object?>("severity", severity));
    }

    public void RecordArtifactGenerated(string harness)
    {
        _artifactsTotal.Add(1, new KeyValuePair<string, object?>("harness", harness));
    }

    public void RecordContainerMetrics(string harness, double cpuPercent, double memoryBytes)
    {
        if (string.IsNullOrWhiteSpace(harness))
            return;

        _containerCpuByHarness[harness] = cpuPercent;
        _containerMemoryByHarness[harness] = memoryBytes;
    }

    private static void UpdateCounter(UpDownCounter<int> counter, ref int field, int count)
    {
        var previous = Interlocked.Exchange(ref field, count);
        var delta = count - previous;
        if (delta != 0)
        {
            counter.Add(delta);
        }
    }

    private IEnumerable<Measurement<double>> GetContainerCpuMeasurements()
    {
        foreach (var (harness, value) in _containerCpuByHarness)
        {
            yield return new Measurement<double>(value, new KeyValuePair<string, object?>("harness", harness));
        }
    }

    private IEnumerable<Measurement<double>> GetContainerMemoryMeasurements()
    {
        foreach (var (harness, value) in _containerMemoryByHarness)
        {
            yield return new Measurement<double>(value, new KeyValuePair<string, object?>("harness", harness));
        }
    }
}
