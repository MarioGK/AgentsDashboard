using AgentsDashboard.ControlPlane.Features.Projects.Components.Pages.Models;
using MudBlazor;

namespace AgentsDashboard.ControlPlane.Components.Pages;

public partial class Dashboard : IDisposable
{
    private static readonly IReadOnlyList<OverviewQuickLink> DefaultQuickLinks =
    [
        new("Runs", "Inspect queue and outcomes", "/settings/runs", Icons.Material.Filled.PlayCircle),
        new("Repositories", "Manage tracked repositories", "/settings/repositories", Icons.Material.Filled.AccountTree),
        new("Task Runtimes", "Inspect worker lifecycle state", "/settings/task-runtimes", Icons.Material.Filled.Engineering),
        new("System", "Tune runtime and retention settings", "/settings/system", Icons.Material.Filled.Tune)
    ];

    private bool _loading = true;
    private int _activeRuns;
    private int _queuedRuns;
    private int _failureRuns;
    private int _workerReadyCount;
    private int _workerTotalCount;
    private string? _workerRuntimeMessage;
    private TaskRuntimeHealthSnapshot _taskRuntimeHealth = TaskRuntimeHealthSnapshot.Empty;
    private string? _taskRuntimeHealthError;
    private DateTime? _lastMetricsRefreshUtc;
    private List<RunDocument> _runs = [];
    private IReadOnlyList<OverviewQuickLink> _quickLinks = DefaultQuickLinks;
    private IDisposable? _selectionSubscription;

    protected override async Task OnInitializedAsync()
    {
        _selectionSubscription = SelectionService.Subscribe(_ => InvokeAsync(LoadDataAsync));
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            await LoadRunsAsync();
            await LoadWorkerReadinessAsync();
            LoadTaskRuntimeHealth();
            _lastMetricsRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task LoadRunsAsync()
    {
        if (SelectionService.SelectedRepositoryId is { Length: > 0 } repositoryId)
        {
            _runs = await RunStore.ListRunsByRepositoryAsync(repositoryId, CancellationToken.None);
        }
        else
        {
            _runs = await RunStore.ListRecentRunsAsync(CancellationToken.None);
        }

        _activeRuns = _runs.Count(run => TaskRunStatusPresentation.FromRunState(run.State).IsWorking);
        _queuedRuns = _runs.Count(run => run.State is RunState.Queued);
        _failureRuns = _runs.Count(run => run.State is RunState.Failed or RunState.Cancelled);
    }

    private async Task LoadWorkerReadinessAsync()
    {
        _workerRuntimeMessage = null;
        _workerReadyCount = 0;
        _workerTotalCount = 0;

        try
        {
            var runtimes = await WorkerLifecycle.ListTaskRuntimesAsync(CancellationToken.None);
            _workerTotalCount = runtimes.Count;
            _workerReadyCount = runtimes.Count(runtime => runtime.LifecycleState is TaskRuntimeLifecycleState.Ready);
        }
        catch
        {
            _workerRuntimeMessage = "Worker runtime metrics unavailable";
        }
    }

    private void LoadTaskRuntimeHealth()
    {
        _taskRuntimeHealthError = null;

        try
        {
            _taskRuntimeHealth = TaskRuntimeHealthSupervisor.GetSnapshot();
        }
        catch
        {
            _taskRuntimeHealth = TaskRuntimeHealthSnapshot.Empty;
            _taskRuntimeHealthError = "Task runtime health snapshot unavailable";
        }
    }

    private string GetScopeText()
    {
        if (SelectionService.SelectedRepository is null)
        {
            return "Showing recent activity across all repositories.";
        }

        return $"Showing recent activity for {SelectionService.SelectedRepository.Name}.";
    }

    private string GetWorkerReadinessText()
    {
        return $"{_workerReadyCount} / {_workerTotalCount}";
    }

    private string GetRuntimeHealthText()
    {
        if (_taskRuntimeHealth.ReadinessBlocked)
        {
            return "Blocked";
        }

        if (_taskRuntimeHealth.UnhealthyRuntimes > 0 || _taskRuntimeHealth.QuarantinedRuntimes > 0)
        {
            return "Warning";
        }

        if (_taskRuntimeHealth.DegradedRuntimes > 0 || _taskRuntimeHealth.RecoveringRuntimes > 0)
        {
            return "Degraded";
        }

        return "Ready";
    }

    private Color GetRuntimeHealthColor()
    {
        if (_taskRuntimeHealth.ReadinessBlocked)
        {
            return Color.Error;
        }

        if (_taskRuntimeHealth.UnhealthyRuntimes > 0 || _taskRuntimeHealth.QuarantinedRuntimes > 0)
        {
            return Color.Warning;
        }

        if (_taskRuntimeHealth.DegradedRuntimes > 0 || _taskRuntimeHealth.RecoveringRuntimes > 0)
        {
            return Color.Info;
        }

        return Color.Success;
    }

    private string GetLastRefreshText()
    {
        if (_lastMetricsRefreshUtc is null)
        {
            return "Not refreshed";
        }

        return _lastMetricsRefreshUtc.Value.ToLocalTime().ToString("g");
    }

    private static string GetRunDisplayId(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return "unknown";
        }

        var length = Math.Min(8, runId.Length);
        return runId[..length];
    }

    public void Dispose()
    {
        _selectionSubscription?.Dispose();
    }
}
