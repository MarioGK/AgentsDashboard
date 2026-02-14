using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentsDashboard.ControlPlane.Services;

public interface IOrchestratorMetrics
{
    void RecordRunStart(string harness, string projectId, string repositoryId);
    void RecordRunComplete(string harness, string state, double durationSeconds, string projectId, string repositoryId);
    void RecordJobDispatch(string harness);
    void RecordError(string errorType, string errorCategory);
    void SetPendingJobs(int count);
    void SetActiveJobs(int count);
    void SetQueuedRuns(int count);
    void SetActiveRuns(int count);
    void RecordQueueWaitTime(double seconds);
    void SetWorkerSlots(string host, int activeSlots, int maxSlots);
    void RecordStatusUpdateLatency(double seconds);
    void RecordProxyRequest(string status, double durationSeconds);
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
    private readonly Counter<long> _proxyRequestsTotal;
    private readonly Counter<long> _proxyErrorsTotal;
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
    private readonly Histogram<double> _proxyDuration;
    private readonly Histogram<double> _grpcDuration;

    private readonly ObservableGauge<int> _workerActiveSlots;
    private readonly ObservableGauge<int> _workerMaxSlots;

    private readonly Dictionary<string, (int Active, int Max)> _workerSlots = new();

    public OrchestratorMetrics()
    {
        _runsTotal = s_meter.CreateCounter<long>("orchestrator_runs_total", "runs", "Total number of runs executed");
        _jobsTotal = s_meter.CreateCounter<long>("orchestrator_jobs_total", "jobs", "Total number of jobs dispatched");
        _errorsTotal = s_meter.CreateCounter<long>("orchestrator_errors_total", "errors", "Total number of errors");
        _proxyRequestsTotal = s_meter.CreateCounter<long>("orchestrator_proxy_requests_total", "requests", "Total proxy requests");
        _proxyErrorsTotal = s_meter.CreateCounter<long>("orchestrator_proxy_errors_total", "errors", "Total proxy errors");
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
        _proxyDuration = s_meter.CreateHistogram<double>("orchestrator_proxy_duration_seconds", "s", "Proxy request duration");
        _grpcDuration = s_meter.CreateHistogram<double>("orchestrator_grpc_duration_seconds", "s", "gRPC call duration");

        _workerActiveSlots = s_meter.CreateObservableGauge<int>("orchestrator_worker_active_slots", () => GetWorkerSlots("active"), "slots", "Active worker slots");
        _workerMaxSlots = s_meter.CreateObservableGauge<int>("orchestrator_worker_max_slots", () => GetWorkerSlots("max"), "slots", "Maximum worker slots");
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

    public void RecordRunStart(string harness, string projectId, string repositoryId)
    {
        var tags = new TagList
        {
            { "harness", harness },
            { "project_id", projectId },
            { "repository_id", repositoryId }
        };
        _runsTotal.Add(1, tags);
    }

    public void RecordRunComplete(string harness, string state, double durationSeconds, string projectId, string repositoryId)
    {
        var tags = new TagList
        {
            { "harness", harness },
            { "state", state },
            { "project_id", projectId },
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
        _pendingJobs.Add(count - GetCurrentPendingJobs());
    }

    public void SetActiveJobs(int count)
    {
        _activeJobs.Add(count - GetCurrentActiveJobs());
    }

    public void SetQueuedRuns(int count)
    {
        _queuedRuns.Add(count - GetCurrentQueuedRuns());
    }

    public void SetActiveRuns(int count)
    {
        _activeRuns.Add(count - GetCurrentActiveRuns());
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

    public void RecordProxyRequest(string status, double durationSeconds)
    {
        _proxyRequestsTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        _proxyDuration.Record(durationSeconds, new KeyValuePair<string, object?>("status", status));

        if (status != "success")
        {
            _proxyErrorsTotal.Add(1, new KeyValuePair<string, object?>("status", status));
        }
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
        _signalRConnections.Add(count - GetCurrentSignalRConnections());
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
        var tags = new TagList
        {
            { "harness", harness }
        };
        s_meter.CreateObservableGauge<double>("orchestrator_container_cpu_percent", () => new Measurement<double>(cpuPercent, tags), "percent", "Container CPU usage");
        s_meter.CreateObservableGauge<double>("orchestrator_container_memory_bytes", () => new Measurement<double>(memoryBytes, tags), "bytes", "Container memory usage");
    }

    private int GetCurrentPendingJobs() => 0;
    private int GetCurrentActiveJobs() => 0;
    private int GetCurrentQueuedRuns() => 0;
    private int GetCurrentActiveRuns() => 0;
    private int GetCurrentSignalRConnections() => 0;
}
