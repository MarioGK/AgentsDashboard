using System.Text;
using System.Text.Json;
using AgentsDashboard.ControlPlane.Components.Shared;
using AgentsDashboard.ControlPlane.Components.Workspace;
using AgentsDashboard.ControlPlane.Components.Workspace.Models;
using BlazorMonaco;
using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;

namespace AgentsDashboard.ControlPlane.Components.Pages;

public partial class Workspace
{
    private const string RepositoryFilterAll = "all";
    private const string RepositoryFilterAttention = "attention";
    private const string RepositoryFilterHealthy = "healthy";

    private const string TaskFilterAll = "all";
    private const string TaskFilterRunning = "running";
    private const string TaskFilterFailed = "failed";
    private const string TaskFilterSucceeded = "succeeded";
    private const string TaskFilterEnabled = "enabled";

    private static readonly string[] ComposerSuggestions =
    [
        "Focus on deterministic output and include validation commands.",
        "Include a concise checklist of changed files and risks.",
        "Validate with dotnet build src/AgentsDashboard.slnx -m --tl and summarize failures.",
        "Finish with a short markdown summary and concrete next steps."
    ];

    private bool _loading = true;
    private List<RepositoryDocument> _repositories = [];
    private RepositoryDocument? _selectedRepository;
    private List<TaskDocument> _selectedRepositoryTasks = [];
    private List<RunDocument> _selectedRepositoryRuns = [];
    private Dictionary<string, RunDocument> _latestRunsByTask = new(StringComparer.OrdinalIgnoreCase);
    private TaskDocument? _selectedTask;
    private RunDocument? _selectedRun;
    private List<RunLogEvent> _selectedRunLogs = [];
    private ParsedHarnessOutput? _selectedRunParsed;
    private List<RunStructuredEventDocument> _selectedRunStructuredEvents = [];
    private RunDiffSnapshotDocument? _selectedRunDiffSnapshot;
    private RunStructuredViewSnapshot _selectedRunStructuredView = new(string.Empty, 0, [], [], [], null, DateTime.UtcNow);
    private RunAiSummaryDocument? _selectedRunAiSummary;
    private List<RunQuestionRequestDocument> _selectedRunQuestionRequests = [];
    private List<WorkspacePromptEntryDocument> _selectedTaskPromptHistory = [];
    private string _repositoryFilter = RepositoryFilterAll;
    private string _taskFilter = TaskFilterAll;
    private int _recentTaskTargetCount = 5;

    private bool _isSubmittingComposer;
    private bool _isSubmittingQuestionAnswers;
    private HarnessExecutionMode? _composerModeOverride;
    private string _composerValue = string.Empty;
    private IReadOnlyList<WorkspaceImageInput> _composerImages = [];
    private string _composerGhostSuggestion = string.Empty;
    private string _composerGhostSuffix = string.Empty;
    private readonly string _composerInputId = $"workspace-composer-{Guid.NewGuid():N}";
    private CancellationTokenSource? _composerSuggestionCts;
    private readonly WorkspaceChatProjectionBuilder _chatProjectionBuilder = new();
    private readonly Dictionary<string, WorkspaceThreadUiCache> _threadUiCacheByTaskId = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkspaceChatMessage> _optimisticMessages = [];
    private bool _leftRailCollapsed;

    private bool _historyPanelOpen;
    private string _historyStateFilter = "all";
    private string _historyModeFilter = "all";
    private string _historyHarnessFilter = "all";

    private bool _promptDraftDialogOpen;
    private bool _isPromptDraftLoading;
    private string _promptDraftError = string.Empty;
    private string _promptDraftValue = string.Empty;
    private int _promptDraftActivePanel;
    private int _promptDraftEditorKey;
    private bool _pendingPromptDraftEditorSync;
    private StandaloneCodeEditor? _promptDraftEditor;
    private PromptDraftMode _promptDraftMode = PromptDraftMode.Generate;
    private readonly DialogOptions _promptDraftDialogOptions = new() { MaxWidth = MaxWidth.False, FullWidth = true, CloseOnEscapeKey = true };

    private IDisposable? _selectionSubscription;
    private IDisposable? _runLogSubscription;
    private IDisposable? _runStatusSubscription;
    private IDisposable? _structuredSubscription;
    private IDisposable? _diffSubscription;
    private IDisposable? _toolSubscription;
    private IJSObjectReference? _workspaceJsModule;
    private DotNetObjectReference<Workspace>? _dotNetRef;
    private string? _viewportListenerHandle;
    private string? _composerKeyBridgeHandle;

    private IReadOnlyList<WorkspaceRepositoryGroup> LeftRailRepositoryGroups
    {
        get
        {
            var filtered = _repositories
                .OrderBy(repo => repo.Name)
                .ToList();

            var attention = filtered.Where(IsAttentionRepository).ToList();
            var healthy = filtered.Where(repo => !IsAttentionRepository(repo)).ToList();
            var groups = new List<WorkspaceRepositoryGroup>();

            if ((_repositoryFilter == RepositoryFilterAll || _repositoryFilter == RepositoryFilterAttention) && attention.Count > 0)
            {
                groups.Add(new WorkspaceRepositoryGroup(
                    "Needs Attention",
                    attention.Select(BuildRepositoryListItem).ToList()));
            }

            if ((_repositoryFilter == RepositoryFilterAll || _repositoryFilter == RepositoryFilterHealthy) && healthy.Count > 0)
            {
                groups.Add(new WorkspaceRepositoryGroup(
                    "Healthy",
                    healthy.Select(BuildRepositoryListItem).ToList()));
            }

            return groups;
        }
    }

    private List<TaskDocument> FilteredRecentTasks
    {
        get
        {
            return _selectedRepositoryTasks
                .Where(MatchesTaskFilter)
                .OrderByDescending(task => GetLatestRun(task.Id)?.CreatedAtUtc ?? task.CreatedAtUtc)
                .Take(_recentTaskTargetCount)
                .ToList();
        }
    }

    private IReadOnlyList<WorkspaceThreadState> ThreadStates =>
        FilteredRecentTasks
            .Select(BuildThreadState)
            .ToList();

    private IReadOnlyList<WorkspaceChatMessage> ProjectedMessages
    {
        get
        {
            var projected = _chatProjectionBuilder.Build(
                    _selectedTaskPromptHistory,
                    _selectedRun,
                    _selectedRunAiSummary,
                    _selectedRunParsed,
                    _selectedRunStructuredView,
                    _selectedRunLogs)
                .ToList();

            if (_optimisticMessages.Count > 0)
            {
                projected.AddRange(_optimisticMessages);
            }

            return projected
                .OrderBy(message => message.TimestampUtc)
                .ThenBy(message => message.Id, StringComparer.Ordinal)
                .ToList();
        }
    }

    private List<RunDocument> SelectedTaskRuns =>
        _selectedTask is null
            ? []
            : _selectedRepositoryRuns
                .Where(run => run.TaskId == _selectedTask.Id)
                .Where(MatchesHistoryRunState)
                .Where(MatchesHistoryRunMode)
                .Where(MatchesHistoryRunHarness)
                .OrderByDescending(run => run.CreatedAtUtc)
                .Take(20)
                .ToList();

    protected override async Task OnInitializedAsync()
    {
        _selectionSubscription = SelectionService.Subscribe(_args =>
        {
            _ = InvokeAsync(OnExternalSelectionChangedAsync);
        });
        _runLogSubscription = UiRealtimeBroker.Subscribe<AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunLogChunkEvent>(OnRunLogChunkAsync);
        _runStatusSubscription = UiRealtimeBroker.Subscribe<AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunStatusChangedEvent>(OnRunStatusChangedAsync);
        _structuredSubscription = UiRealtimeBroker.Subscribe<AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunStructuredEventChangedEvent>(OnRunStructuredEventChangedAsync);
        _diffSubscription = UiRealtimeBroker.Subscribe<AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunDiffUpdatedEvent>(OnRunDiffUpdatedAsync);
        _toolSubscription = UiRealtimeBroker.Subscribe<AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunToolTimelineUpdatedEvent>(OnRunToolTimelineUpdatedAsync);
        RefreshComposerSuggestionFallback();
        await LoadWorkspaceAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            _workspaceJsModule = await JS.InvokeAsync<IJSObjectReference>("import", "./workspace.js");
            var viewportHeight = await _workspaceJsModule.InvokeAsync<int>("getViewportHeight");
            SetRecentTaskTargetCount(viewportHeight);
            _viewportListenerHandle = await _workspaceJsModule.InvokeAsync<string?>("registerViewportListener", _dotNetRef);
            await InvokeAsync(StateHasChanged);
        }

        if (_workspaceJsModule is not null)
        {
            if (ShouldAttachComposerBridge() && _composerKeyBridgeHandle is null)
            {
                _composerKeyBridgeHandle = await _workspaceJsModule.InvokeAsync<string?>("registerComposerKeyBridge", _composerInputId, _dotNetRef);
            }
            else if (!ShouldAttachComposerBridge() && _composerKeyBridgeHandle is not null)
            {
                await _workspaceJsModule.InvokeVoidAsync("unregisterComposerKeyBridge", _composerKeyBridgeHandle);
                _composerKeyBridgeHandle = null;
            }
        }

        if (_promptDraftDialogOpen && _pendingPromptDraftEditorSync && _promptDraftEditor is not null)
        {
            _pendingPromptDraftEditorSync = false;
            await _promptDraftEditor.SetValue(_promptDraftValue);
        }
    }

    [JSInvokable]
    public Task OnWorkspaceViewportChanged(int viewportHeight)
    {
        SetRecentTaskTargetCount(viewportHeight);
        return InvokeAsync(StateHasChanged);
    }

    private void ToggleLeftRail()
    {
        _leftRailCollapsed = !_leftRailCollapsed;
    }

    private Task OnRepositoryFilterChangedAsync(string filter)
    {
        _repositoryFilter = string.IsNullOrWhiteSpace(filter) ? RepositoryFilterAll : filter;
        return Task.CompletedTask;
    }

    private Task OnTaskFilterChangedAsync(string filter)
    {
        _taskFilter = string.IsNullOrWhiteSpace(filter) ? TaskFilterAll : filter;
        return Task.CompletedTask;
    }

    private Task SelectRepositoryFromRailAsync(string repositoryId)
    {
        return SelectRepositoryAsync(repositoryId, syncSelection: true);
    }

    private Task OnPlanModeChangedAsync(bool enabled)
    {
        _composerModeOverride = enabled ? HarnessExecutionMode.Plan : null;
        return Task.CompletedTask;
    }

    private Task RefreshSelectedRunSummaryAsync()
    {
        return RefreshRunSummaryAsync(force: true);
    }

    private Task OpenImprovePromptDraftDialogAsync()
    {
        return OpenPromptDraftDialogAsync(PromptDraftMode.Improve);
    }

    private Task OpenGeneratePromptDraftDialogAsync()
    {
        return OpenPromptDraftDialogAsync(PromptDraftMode.Generate);
    }

    private void CloseHistoryPanel()
    {
        _historyPanelOpen = false;
    }

    private string GetWorkspaceShellClass()
    {
        return _leftRailCollapsed
            ? "workspace-chat-shell workspace-chat-shell-rail-collapsed"
            : "workspace-chat-shell";
    }

    private string GetActiveTaskTitle()
    {
        if (_selectedTask is not null)
        {
            return _selectedTask.Name;
        }

        return _selectedRepository is null
            ? "Workspace"
            : $"New Task in {_selectedRepository.Name}";
    }

    private string GetActiveTaskHarness()
    {
        if (_selectedTask is not null)
        {
            return _selectedTask.Harness;
        }

        return _selectedRepository?.TaskDefaults.Harness ?? "codex";
    }

    private string GetActiveTaskStateLabel()
    {
        if (_selectedTask is null)
        {
            return "Ready to start";
        }

        return GetTaskStateLabel(_selectedTask);
    }

    private Color GetActiveTaskStateColor()
    {
        if (_selectedTask is null)
        {
            return Color.Info;
        }

        return GetTaskStateColor(_selectedTask);
    }

    private string GetSelectedRunShortId()
    {
        if (_selectedRun is null || string.IsNullOrWhiteSpace(_selectedRun.Id))
        {
            return string.Empty;
        }

        return _selectedRun.Id[..Math.Min(8, _selectedRun.Id.Length)];
    }

    private string GetSelectedRunStateLabel()
    {
        return _selectedRun?.State.ToString() ?? string.Empty;
    }

    private Color GetSelectedRunStateColor()
    {
        return _selectedRun is null
            ? Color.Default
            : GetStateColor(_selectedRun.State);
    }

    private bool IsSelectedRunActive =>
        _selectedRun is not null && _selectedRun.State is RunState.Running or RunState.Queued or RunState.PendingApproval;

    [JSInvokable]
    public Task<bool> TryAcceptGhostSuggestionFromJs(string key, int selectionStart, int selectionEnd)
    {
        var accepted = (key == "Tab" || key == "ArrowRight") && TryAcceptGhostSuggestion(selectionStart, selectionEnd);
        if (accepted)
        {
            _ = InvokeAsync(StateHasChanged);
        }

        return Task.FromResult(accepted);
    }

    [JSInvokable]
    public async Task<bool> TrySubmitComposerFromJs()
    {
        if (_isSubmittingComposer)
        {
            return false;
        }

        if (_selectedRepository is null)
        {
            return false;
        }

        await SubmitComposerAsync();
        return true;
    }

    private async Task LoadWorkspaceAsync()
    {
        _loading = true;
        await InvokeAsync(StateHasChanged);

        try
        {
            _repositories = await RepositoryStore.ListRepositoriesAsync(CancellationToken.None);

            var selectedRepositoryId = SelectionService.SelectedRepositoryId;
            if (string.IsNullOrWhiteSpace(selectedRepositoryId) || _repositories.All(repo => repo.Id != selectedRepositoryId))
            {
                selectedRepositoryId = _repositories.FirstOrDefault()?.Id;
            }

            if (!string.IsNullOrWhiteSpace(selectedRepositoryId))
            {
                await SelectRepositoryAsync(selectedRepositoryId, false);
            }
            else
            {
                _selectedRepository = null;
                _selectedRepositoryTasks = [];
                _selectedRepositoryRuns = [];
                _latestRunsByTask = new Dictionary<string, RunDocument>(StringComparer.OrdinalIgnoreCase);
                _selectedTask = null;
                _selectedRun = null;
                _selectedRunLogs = [];
                _selectedRunParsed = null;
                ClearSelectedRunStructuredState();
                _selectedRunAiSummary = null;
                _selectedRunQuestionRequests = [];
                _selectedTaskPromptHistory = [];
                _composerValue = string.Empty;
                _composerImages = [];
                _threadUiCacheByTaskId.Clear();
            }
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to load workspace: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnExternalSelectionChangedAsync()
    {
        var selectedRepositoryId = SelectionService.SelectedRepositoryId;
        if (string.IsNullOrWhiteSpace(selectedRepositoryId) || selectedRepositoryId == _selectedRepository?.Id)
        {
            return;
        }

        if (_repositories.All(repo => repo.Id != selectedRepositoryId))
        {
            _repositories = await RepositoryStore.ListRepositoriesAsync(CancellationToken.None);
        }

        await SelectRepositoryAsync(selectedRepositoryId, false);
    }

    private async Task SelectRepositoryAsync(string repositoryId, bool syncSelection)
    {
        var repository = _repositories.FirstOrDefault(repo => repo.Id == repositoryId);
        if (repository is null)
        {
            return;
        }

        _selectedRepository = repository;
        _selectedTask = null;
        _selectedRun = null;
        _selectedRunLogs = [];
        _selectedRunParsed = null;
        ClearSelectedRunStructuredState();
        _selectedRunAiSummary = null;
        _selectedRunQuestionRequests = [];
        _selectedTaskPromptHistory = [];
        _composerValue = string.Empty;
        _composerImages = [];
        _composerGhostSuggestion = string.Empty;
        _composerGhostSuffix = string.Empty;
        _threadUiCacheByTaskId.Clear();
        _optimisticMessages.Clear();
        _historyPanelOpen = false;

        if (syncSelection && SelectionService.SelectedRepositoryId != repositoryId)
        {
            await SelectionService.SelectRepositoryAsync(repositoryId, CancellationToken.None);
        }

        await LoadSelectedRepositoryDataAsync(repositoryId, preserveTaskSelection: false);
    }

    private async Task LoadSelectedRepositoryDataAsync(string repositoryId, bool preserveTaskSelection)
    {
        var previousTaskId = preserveTaskSelection ? _selectedTask?.Id : null;
        var previousRunId = preserveTaskSelection ? _selectedRun?.Id : null;
        try
        {
            _selectedRepositoryTasks = await TaskStore.ListTasksAsync(repositoryId, CancellationToken.None);
            _selectedRepositoryRuns = await RunStore.ListRunsByRepositoryAsync(repositoryId, CancellationToken.None);
            _selectedRepositoryRuns = _selectedRepositoryRuns.OrderByDescending(run => run.CreatedAtUtc).ToList();

            RebuildLatestRunsIndex();
            PruneThreadCaches();

            if (!string.IsNullOrWhiteSpace(previousTaskId) && _selectedRepositoryTasks.Any(task => task.Id == previousTaskId))
            {
                await SelectTaskAsync(previousTaskId, preserveRunSelection: true, preferredRunId: previousRunId);
                return;
            }

            var defaultTask = _selectedRepositoryTasks
                .OrderByDescending(task => GetLatestRun(task.Id)?.CreatedAtUtc ?? task.CreatedAtUtc)
                .FirstOrDefault();

            if (defaultTask is null)
            {
                _selectedTask = null;
                _selectedRun = null;
                _selectedRunLogs = [];
                _selectedRunParsed = null;
                ClearSelectedRunStructuredState();
                _selectedRunAiSummary = null;
                _selectedTaskPromptHistory = [];
                _composerValue = string.Empty;
                _composerImages = [];
                _optimisticMessages.Clear();
                return;
            }

            await SelectTaskAsync(defaultTask.Id, preserveRunSelection: false, preferredRunId: null);
        }
        finally
        {
        }
    }

    private Task SelectTaskAsync(string taskId)
    {
        return SelectTaskAsync(taskId, preserveRunSelection: false, preferredRunId: null);
    }

    private async Task SelectTaskAsync(string taskId, bool preserveRunSelection, string? preferredRunId)
    {
        var task = _selectedRepositoryTasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null)
        {
            return;
        }

        PersistActiveThreadCache();
        _selectedTask = task;
        _composerModeOverride = null;
        _optimisticMessages.Clear();
        RestoreThreadDraft(task.Id);
        _selectedTaskPromptHistory = await RunStore.ListWorkspacePromptHistoryAsync(task.Id, 80, CancellationToken.None);

        var taskRuns = GetRunsForTask(task.Id);
        RunDocument? selectedRun = null;
        var restoredRunId = GetThreadCache(task.Id)?.SelectedRunId;
        var runPreference = string.IsNullOrWhiteSpace(preferredRunId) ? restoredRunId : preferredRunId;

        if (!string.IsNullOrWhiteSpace(runPreference) && (preserveRunSelection || !string.IsNullOrWhiteSpace(restoredRunId)))
        {
            selectedRun = taskRuns.FirstOrDefault(run => run.Id == runPreference);
        }

        selectedRun ??= taskRuns
            .Where(run => run.State is RunState.Running or RunState.Queued or RunState.PendingApproval)
            .OrderByDescending(run => run.CreatedAtUtc)
            .FirstOrDefault();
        selectedRun ??= taskRuns
            .Where(run => run.State is RunState.Succeeded or RunState.Failed or RunState.Cancelled)
            .OrderByDescending(run => run.EndedAtUtc ?? run.CreatedAtUtc)
            .FirstOrDefault();
        selectedRun ??= taskRuns.FirstOrDefault();

        if (selectedRun is null)
        {
            _selectedRun = null;
            _selectedRunLogs = [];
            _selectedRunParsed = null;
            ClearSelectedRunStructuredState();
            _selectedRunAiSummary = null;
            SetThreadSelectedRun(task.Id, string.Empty);
        }
        else
        {
            await SelectRunAsync(selectedRun.Id);
        }

        MarkThreadActivity(task.Id, DateTime.UtcNow, false);
        _ = QueueComposerSuggestionAsync();
    }

    private async Task SelectRunAsync(string runId)
    {
        var run = _selectedRepositoryRuns.FirstOrDefault(item => item.Id == runId);
        if (run is null)
        {
            _selectedRun = null;
            _selectedRunLogs = [];
            _selectedRunParsed = null;
            ClearSelectedRunStructuredState();
            _selectedRunAiSummary = null;
            if (_selectedTask is not null)
            {
                SetThreadSelectedRun(_selectedTask.Id, string.Empty);
            }
            return;
        }

        _selectedRun = run;
        _selectedRunLogs = await RunStore.ListRunLogsAsync(run.Id, CancellationToken.None);
        _selectedRunLogs = _selectedRunLogs.OrderBy(log => log.TimestampUtc).ToList();
        _selectedRunParsed = HarnessOutputParser.Parse(_selectedRun.OutputJson, _selectedRunLogs);
        await RefreshSelectedRunStructuredStateAsync(run.Id);
        await RefreshSelectedRunQuestionRequestsAsync(run.Id, run.TaskId);
        _selectedRunAiSummary = await RunStore.GetRunAiSummaryAsync(run.Id, CancellationToken.None);
        await RefreshRunSummaryAsync(force: false);
        SetThreadSelectedRun(run.TaskId, run.Id);
        MarkThreadActivity(run.TaskId, DateTime.UtcNow, false);
    }

    private async Task OnRunLogChunkAsync(AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunLogChunkEvent logEvent)
    {
        var run = _selectedRepositoryRuns.FirstOrDefault(item => string.Equals(item.Id, logEvent.RunId, StringComparison.OrdinalIgnoreCase));
        if (run is not null)
        {
            var isActiveThread = _selectedTask is not null && string.Equals(_selectedTask.Id, run.TaskId, StringComparison.OrdinalIgnoreCase);
            MarkThreadActivity(run.TaskId, logEvent.Timestamp, !isActiveThread);
        }

        if (_selectedRun is null || !string.Equals(logEvent.RunId, _selectedRun.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedRunLogs.Add(new RunLogEvent
        {
            RunId = logEvent.RunId,
            Level = logEvent.Level,
            Message = logEvent.Message,
            TimestampUtc = logEvent.Timestamp,
        });
        _selectedRunLogs = _selectedRunLogs.OrderBy(log => log.TimestampUtc).ToList();
        _selectedRunParsed = HarnessOutputParser.Parse(_selectedRun.OutputJson, _selectedRunLogs);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRunStatusChangedAsync(AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunStatusChangedEvent statusEvent)
    {
        if (_selectedRepository is null)
        {
            return;
        }

        var refreshed = await RunStore.GetRunAsync(statusEvent.RunId, CancellationToken.None);
        if (refreshed is null || !string.Equals(refreshed.RepositoryId, _selectedRepository.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existingIndex = _selectedRepositoryRuns.FindIndex(run => string.Equals(run.Id, refreshed.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _selectedRepositoryRuns[existingIndex] = refreshed;
        }
        else
        {
            _selectedRepositoryRuns.Add(refreshed);
        }

        _selectedRepositoryRuns = _selectedRepositoryRuns
            .OrderByDescending(run => run.CreatedAtUtc)
            .ToList();
        RebuildLatestRunsIndex();
        var isActiveThread = _selectedTask is not null && string.Equals(_selectedTask.Id, refreshed.TaskId, StringComparison.OrdinalIgnoreCase);
        MarkThreadActivity(refreshed.TaskId, DateTime.UtcNow, !isActiveThread);
        SetThreadSelectedRun(refreshed.TaskId, refreshed.Id);

        if (_selectedRun is not null && string.Equals(_selectedRun.Id, refreshed.Id, StringComparison.OrdinalIgnoreCase))
        {
            _selectedRun = refreshed;
            _selectedRunParsed = HarnessOutputParser.Parse(_selectedRun.OutputJson, _selectedRunLogs);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRunStructuredEventChangedAsync(AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunStructuredEventChangedEvent structuredEvent)
    {
        var run = _selectedRepositoryRuns.FirstOrDefault(item => string.Equals(item.Id, structuredEvent.RunId, StringComparison.OrdinalIgnoreCase));
        if (run is not null)
        {
            var isActiveThread = _selectedTask is not null && string.Equals(_selectedTask.Id, run.TaskId, StringComparison.OrdinalIgnoreCase);
            MarkThreadActivity(run.TaskId, DateTime.UtcNow, !isActiveThread);
        }

        if (_selectedRun is null || !string.Equals(_selectedRun.Id, structuredEvent.RunId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RefreshSelectedRunStructuredStateAsync(structuredEvent.RunId);
        await RefreshSelectedRunQuestionRequestsAsync(structuredEvent.RunId, _selectedRun.TaskId);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRunDiffUpdatedAsync(AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunDiffUpdatedEvent diffEvent)
    {
        var run = _selectedRepositoryRuns.FirstOrDefault(item => string.Equals(item.Id, diffEvent.RunId, StringComparison.OrdinalIgnoreCase));
        if (run is not null)
        {
            var isActiveThread = _selectedTask is not null && string.Equals(_selectedTask.Id, run.TaskId, StringComparison.OrdinalIgnoreCase);
            MarkThreadActivity(run.TaskId, DateTime.UtcNow, !isActiveThread);
        }

        if (_selectedRun is null || !string.Equals(_selectedRun.Id, diffEvent.RunId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RefreshSelectedRunStructuredStateAsync(diffEvent.RunId);
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRunToolTimelineUpdatedAsync(AgentsDashboard.Contracts.Features.Realtime.Models.Events.RunToolTimelineUpdatedEvent toolEvent)
    {
        var run = _selectedRepositoryRuns.FirstOrDefault(item => string.Equals(item.Id, toolEvent.RunId, StringComparison.OrdinalIgnoreCase));
        if (run is not null)
        {
            var isActiveThread = _selectedTask is not null && string.Equals(_selectedTask.Id, run.TaskId, StringComparison.OrdinalIgnoreCase);
            MarkThreadActivity(run.TaskId, DateTime.UtcNow, !isActiveThread);
        }

        if (_selectedRun is null || !string.Equals(_selectedRun.Id, toolEvent.RunId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await RefreshSelectedRunStructuredStateAsync(toolEvent.RunId);
        await RefreshSelectedRunQuestionRequestsAsync(toolEvent.RunId, _selectedRun.TaskId);
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshSelectedRunStructuredStateAsync(string? expectedRunId = null)
    {
        var runId = expectedRunId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            runId = _selectedRun?.Id;
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            ClearSelectedRunStructuredState();
            return;
        }

        var structuredEvents = await RunStore.ListRunStructuredEventsAsync(runId, 2000, CancellationToken.None);
        var diffSnapshot = await RunStore.GetLatestRunDiffSnapshotAsync(runId, CancellationToken.None);
        var structuredView = await RunStructuredViewService.GetViewAsync(runId, CancellationToken.None);

        if (_selectedRun is null || !string.Equals(_selectedRun.Id, runId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedRunStructuredEvents = structuredEvents;
        _selectedRunDiffSnapshot = diffSnapshot;
        _selectedRunStructuredView = structuredView;
    }

    private async Task RefreshSelectedRunQuestionRequestsAsync(string? expectedRunId = null, string? expectedTaskId = null)
    {
        var runId = expectedRunId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            runId = _selectedRun?.Id;
        }

        var taskId = expectedTaskId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            taskId = _selectedTask?.Id;
        }

        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(taskId))
        {
            _selectedRunQuestionRequests = [];
            return;
        }

        var pendingQuestionRequests = await RunStore.ListPendingRunQuestionRequestsAsync(taskId, runId, CancellationToken.None);
        if (_selectedRun is null || !string.Equals(_selectedRun.Id, runId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedRunQuestionRequests = pendingQuestionRequests
            .OrderBy(request => request.SourceSequence)
            .ThenBy(request => request.CreatedAtUtc)
            .ToList();
    }

    private async Task RefreshRunSummaryAsync(bool force)
    {
        if (_selectedRepository is null || _selectedRun is null)
        {
            return;
        }

        var result = await WorkspaceService.RefreshRunSummaryAsync(
            _selectedRepository.Id,
            _selectedRun.Id,
            eventType: "run-selected",
            force,
            CancellationToken.None);

        if (!result.Success)
        {
            return;
        }

        _selectedRunAiSummary = await RunStore.GetRunAiSummaryAsync(_selectedRun.Id, CancellationToken.None)
            ?? _selectedRunAiSummary;

        if (_selectedRunAiSummary is null && !string.IsNullOrWhiteSpace(result.Summary))
        {
            _selectedRunAiSummary = new RunAiSummaryDocument
            {
                RunId = _selectedRun.Id,
                RepositoryId = _selectedRun.RepositoryId,
                TaskId = _selectedRun.TaskId,
                Title = "Run summary",
                Summary = result.Summary,
                Model = result.UsedFallback ? "fallback" : "glm-5",
                GeneratedAtUtc = DateTime.UtcNow,
            };
        }

        if (_selectedTask is not null)
        {
            MarkThreadActivity(_selectedTask.Id, DateTime.UtcNow, false);
        }
    }

    private async Task RefreshSelectedRepositoryAsync()
    {
        if (_selectedRepository is null)
        {
            return;
        }

        PersistActiveThreadCache();
        await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: true);
    }

    private async Task DeleteTaskAsync(string taskId)
    {
        if (_selectedRepository is null)
        {
            return;
        }

        var task = _selectedRepositoryTasks.FirstOrDefault(item => string.Equals(item.Id, taskId, StringComparison.OrdinalIgnoreCase));
        if (task is null)
        {
            return;
        }

        var confirmed = await DialogService.ShowMessageBox(
            "Delete Task",
            $"Delete task '{task.Name}' and all related task data?",
            yesText: "Delete",
            cancelText: "Cancel");

        if (confirmed != true)
        {
            return;
        }

        try
        {
            var result = await TaskStore.DeleteTaskCascadeAsync(taskId, CancellationToken.None);
            if (!result.TaskDeleted)
            {
                Snackbar.Add("Task was already deleted or not found.", Severity.Warning);
            }
            else
            {
                var details = new List<string>();

                if (result.DeletedRuns > 0)
                {
                    details.Add($"{result.DeletedRuns} run(s)");
                }

                if (result.DeletedPromptEntries > 0)
                {
                    details.Add($"{result.DeletedPromptEntries} prompt history entries");
                }

                if (result.DeletedRunLogs > 0)
                {
                    details.Add($"{result.DeletedRunLogs} run log line(s)");
                }

                if (result.DeletedRunSummaries > 0)
                {
                    details.Add($"{result.DeletedRunSummaries} summary(s)");
                }

                if (result.DeletedTaskWorkspaceDirectories > 0)
                {
                    details.Add($"{result.DeletedTaskWorkspaceDirectories} workspace directory(ies)");
                }

                if (details.Count == 0)
                {
                    Snackbar.Add("Task deleted.", Severity.Success);
                }
                else
                {
                    Snackbar.Add($"Task deleted with {string.Join(", ", details)}.", Severity.Success);
                }

                _threadUiCacheByTaskId.Remove(taskId);
            }

            await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: true);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to delete task: {ex.Message}", Severity.Error);
        }
    }

    private async Task StartNewTaskComposerAsync()
    {
        if (_selectedRepository is null)
        {
            if (_repositories.Count == 0)
            {
                Snackbar.Add("Create a repository first.", Severity.Warning);
                return;
            }

            await SelectRepositoryAsync(_repositories[0].Id, true);
        }

        PersistActiveThreadCache();
        _selectedTask = null;
        _selectedRun = null;
        _selectedRunLogs = [];
        _selectedRunParsed = null;
        _selectedRunStructuredEvents = [];
        _selectedRunDiffSnapshot = null;
        _selectedRunStructuredView = new RunStructuredViewSnapshot(string.Empty, 0, [], [], [], null, DateTime.UtcNow);
        _selectedRunAiSummary = null;
        _selectedRunQuestionRequests = [];
        _selectedTaskPromptHistory = [];
        _composerValue = string.Empty;
        _composerImages = [];
        _composerGhostSuggestion = string.Empty;
        _composerGhostSuffix = string.Empty;
        _composerModeOverride = null;
        _optimisticMessages.Clear();
        _historyPanelOpen = false;
        await InvokeAsync(StateHasChanged);
    }

    private async Task<string?> CreateTaskFromComposerAsync(
        string composerText,
        IReadOnlyList<WorkspaceImageInput> composerImages)
    {
        if (_selectedRepository is null)
        {
            return null;
        }

        var taskPrompt = AppendCreateTaskPromptImageReferences(composerText, composerImages).Trim();
        if (string.IsNullOrWhiteSpace(taskPrompt))
        {
            Snackbar.Add("Task prompt is required.", Severity.Warning);
            return null;
        }

        try
        {
            var titleResult = await WorkspaceAiService.GenerateTaskTitleAsync(_selectedRepository.Id, taskPrompt, CancellationToken.None);
            var taskName = titleResult.Success && !string.IsNullOrWhiteSpace(titleResult.Text)
                ? titleResult.Text
                : BuildFallbackTaskName(taskPrompt);

            var request = new CreateTaskRequest(
                RepositoryId: _selectedRepository.Id,
                Prompt: taskPrompt,
                Name: taskName);

            var created = await TaskStore.CreateTaskAsync(request, CancellationToken.None);
            await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: false);
            await SelectTaskAsync(created.Id);

            if (titleResult.UsedFallback && !string.IsNullOrWhiteSpace(titleResult.Message))
            {
                Snackbar.Add(titleResult.Message, Severity.Warning);
            }

            Snackbar.Add("Task created.", Severity.Success);
            return created.Id;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to create task: {ex.Message}", Severity.Error);
            return null;
        }
    }

    private async Task SubmitComposerAsync()
    {
        if (_selectedRepository is null)
        {
            return;
        }

        var composerText = _composerValue.Trim();
        var hasImages = _composerImages.Count > 0;
        if (string.IsNullOrWhiteSpace(composerText) && !hasImages)
        {
            Snackbar.Add("Enter prompt guidance first.", Severity.Info);
            return;
        }

        var persistedValue = _composerValue;
        var persistedImages = _composerImages.ToList();
        var persistedGhostSuggestion = _composerGhostSuggestion;
        var persistedGhostSuffix = _composerGhostSuffix;
        var submittedImages = _composerImages.ToList();

        void RestoreComposerDraft()
        {
            _composerValue = persistedValue;
            _composerImages = persistedImages;
            _composerGhostSuggestion = persistedGhostSuggestion;
            _composerGhostSuffix = persistedGhostSuffix;
            UpdateActiveThreadCache();
            _ = QueueComposerSuggestionAsync();
        }

        _isSubmittingComposer = true;
        var optimisticMessageId = AddOptimisticUserMessage(composerText, _composerImages.Count);

        try
        {
            _composerValue = string.Empty;
            _composerImages = [];
            _composerGhostSuggestion = string.Empty;
            _composerGhostSuffix = string.Empty;
            UpdateActiveThreadCache();

            if (_selectedTask is null)
            {
                var createdTaskId = await CreateTaskFromComposerAsync(composerText, submittedImages);
                if (!string.IsNullOrWhiteSpace(createdTaskId))
                {
                    RemoveOptimisticUserMessage(optimisticMessageId);
                    _ = QueueComposerSuggestionAsync();
                }
                else
                {
                    RestoreComposerDraft();
                    RemoveOptimisticUserMessage(optimisticMessageId);
                }

                return;
            }

            var submission = await WorkspaceService.SubmitPromptAsync(
                _selectedRepository.Id,
                new WorkspacePromptSubmissionRequest(
                    Prompt: MergePrompt(_selectedTask.Prompt, composerText),
                    TaskId: _selectedTask.Id,
                    Harness: _selectedTask.Harness,
                    Command: _selectedTask.Command,
                    ForceNewRun: true,
                    UserMessage: composerText,
                    ModeOverride: _composerModeOverride,
                    Images: submittedImages,
                    SessionProfileId: null),
                CancellationToken.None);

            if (!submission.Success)
            {
                RestoreComposerDraft();
                RemoveOptimisticUserMessage(optimisticMessageId);
                Snackbar.Add(submission.Message, Severity.Warning);
                return;
            }

            if (submission.Task is not null)
            {
                _selectedTask = submission.Task;
            }

            _ = QueueComposerSuggestionAsync();
            RemoveOptimisticUserMessage(optimisticMessageId);

            await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: true);

            if (submission.Run is not null)
            {
                await SelectRunAsync(submission.Run.Id);
            }

            Snackbar.Add(submission.Message, submission.DispatchAccepted ? Severity.Success : Severity.Info);
        }
        catch (Exception ex)
        {
            RestoreComposerDraft();
            RemoveOptimisticUserMessage(optimisticMessageId);
            Snackbar.Add($"Submit failed: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isSubmittingComposer = false;
        }
    }

    private string AddOptimisticUserMessage(string composerText, int imageCount)
    {
        var normalizedText = string.IsNullOrWhiteSpace(composerText)
            ? $"Sent {imageCount} image(s)."
            : composerText;

        var message = new WorkspaceChatMessage(
            Id: $"optimistic-{Guid.NewGuid():N}",
            Kind: WorkspaceChatMessageKind.User,
            Title: "You",
            Content: normalizedText,
            TimestampUtc: DateTime.UtcNow,
            Meta: "Sending...");
        _optimisticMessages.Add(message);
        return message.Id;
    }

    private void RemoveOptimisticUserMessage(string? optimisticMessageId)
    {
        if (string.IsNullOrWhiteSpace(optimisticMessageId))
        {
            return;
        }

        _optimisticMessages.RemoveAll(message =>
            string.Equals(message.Id, optimisticMessageId, StringComparison.Ordinal));
    }

    private async Task SubmitQuestionAnswersAsync(WorkspaceQuestionAnswersSubmissionRequest request)
    {
        if (_selectedRepository is null || _selectedTask is null || _selectedRun is null || _isSubmittingQuestionAnswers)
        {
            return;
        }

        _isSubmittingQuestionAnswers = true;
        try
        {
            var result = await WorkspaceService.SubmitQuestionAnswersAsync(
                _selectedRepository.Id,
                request,
                CancellationToken.None);

            if (!result.Success)
            {
                Snackbar.Add(result.Message, Severity.Warning);
                await RefreshSelectedRunQuestionRequestsAsync(_selectedRun.Id, _selectedTask.Id);
                return;
            }

            _selectedRunQuestionRequests = _selectedRunQuestionRequests
                .Where(questionRequest => !string.Equals(questionRequest.Id, request.QuestionRequestId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: true);
            if (result.Run is not null)
            {
                await SelectRunAsync(result.Run.Id);
            }

            Snackbar.Add(result.Message, Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to submit answers: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isSubmittingQuestionAnswers = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task OpenPromptDraftDialogAsync(PromptDraftMode mode)
    {
        if (_selectedRepository is null || _selectedTask is null)
        {
            Snackbar.Add("Select a task first.", Severity.Warning);
            return;
        }

        _promptDraftMode = mode;
        _promptDraftDialogOpen = true;
        _isPromptDraftLoading = true;
        _promptDraftError = string.Empty;
        _promptDraftValue = string.Empty;
        _promptDraftActivePanel = 0;
        _promptDraftEditorKey++;
        _pendingPromptDraftEditorSync = false;
        StateHasChanged();

        try
        {
            WorkspaceAiTextResult result;
            if (mode == PromptDraftMode.Improve)
            {
                result = await WorkspaceAiService.ImprovePromptAsync(
                    _selectedRepository.Id,
                    _selectedTask.Prompt,
                    _composerValue,
                    CancellationToken.None);
            }
            else
            {
                result = await WorkspaceAiService.GeneratePromptFromContextAsync(
                    _selectedRepository.Id,
                    BuildPromptGenerationContext(),
                    CancellationToken.None);
            }

            if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            {
                _promptDraftError = result.Message ?? "Prompt generation failed.";
                return;
            }

            _promptDraftValue = result.Text;
            _pendingPromptDraftEditorSync = true;
        }
        catch (Exception ex)
        {
            _promptDraftError = $"Prompt generation failed: {ex.Message}";
        }
        finally
        {
            _isPromptDraftLoading = false;
            StateHasChanged();
        }
    }

    private void ClosePromptDraftDialog()
    {
        _promptDraftDialogOpen = false;
        _isPromptDraftLoading = false;
        _promptDraftError = string.Empty;
    }

    private async Task RefreshPromptDraftPreviewAsync()
    {
        if (_promptDraftEditor is not null)
        {
            _promptDraftValue = await _promptDraftEditor.GetValue();
        }
    }

    private async Task ApplyPromptDraftAsync()
    {
        if (_selectedTask is null || _selectedRepository is null)
        {
            return;
        }

        var promptValue = _promptDraftEditor is not null
            ? await _promptDraftEditor.GetValue()
            : _promptDraftValue;

        if (string.IsNullOrWhiteSpace(promptValue))
        {
            Snackbar.Add("Draft is empty.", Severity.Warning);
            return;
        }

        try
        {
            var updateRequest = ToUpdateRequest(_selectedTask, promptValue);
            var updatedTask = await TaskStore.UpdateTaskAsync(_selectedTask.Id, updateRequest, CancellationToken.None);
            if (updatedTask is not null)
            {
                _selectedTask = updatedTask;
            }

            _promptDraftDialogOpen = false;
            await LoadSelectedRepositoryDataAsync(_selectedRepository.Id, preserveTaskSelection: true);
            Snackbar.Add("Task prompt updated.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to apply prompt: {ex.Message}", Severity.Error);
        }
    }

    private string GetPromptDraftTitle()
    {
        return _promptDraftMode switch
        {
            PromptDraftMode.Improve => "Improve Prompt Draft",
            _ => "Generate Prompt Draft"
        };
    }

    private string BuildPromptGenerationContext()
    {
        return $"""
Task: {_selectedTask?.Name}
Harness: {_selectedTask?.Harness}
Command: {_selectedTask?.Command}
Existing Prompt:
{_selectedTask?.Prompt}

Composer Focus:
{_composerValue}
""";
    }

    private StandaloneEditorConstructionOptions DraftEditorOptions(StandaloneCodeEditor editor) => new()
    {
        AutomaticLayout = true,
        Language = "markdown",
        Theme = "agents-dashboard-dark",
        Minimap = new EditorMinimapOptions { Enabled = false },
        WordWrap = "on",
        FontFamily = "\"JetBrains Mono\", SFMono-Regular, monospace",
        FontLigatures = true,
        FontSize = 13,
        Value = _promptDraftValue,
    };

    private async Task OnComposerValueChangedAsync(string value)
    {
        _composerValue = value;
        UpdateActiveThreadCache();
        await QueueComposerSuggestionAsync();
    }

    private Task OnComposerImagesChangedAsync(IReadOnlyList<WorkspaceImageInput> images)
    {
        _composerImages = images;
        UpdateActiveThreadCache();
        return Task.CompletedTask;
    }

    private Task ShowComposerValidationAsync(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Snackbar.Add(message, Severity.Warning);
        }

        return Task.CompletedTask;
    }

    private async Task QueueComposerSuggestionAsync()
    {
        RefreshComposerSuggestionFallback();

        _composerSuggestionCts?.Cancel();
        _composerSuggestionCts?.Dispose();

        if (_selectedRepository is null || _selectedTask is null)
        {
            return;
        }

        if (_composerValue.Length < 12)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _composerSuggestionCts = cts;

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), cts.Token);

            if (_workspaceJsModule is not null)
            {
                var selection = await _workspaceJsModule.InvokeAsync<int[]>("getInputSelection", _composerInputId);
                if (selection.Length >= 2)
                {
                    var atEnd = selection[0] == _composerValue.Length && selection[1] == _composerValue.Length;
                    if (!atEnd)
                    {
                        return;
                    }
                }
            }

            var result = await WorkspaceAiService.SuggestPromptContinuationAsync(
                _selectedRepository.Id,
                _composerValue,
                BuildPromptGenerationContext(),
                cts.Token);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Text))
            {
                return;
            }

            if (ReferenceEquals(_composerSuggestionCts, cts) && result.Text.Length > 0)
            {
                if (result.Text.StartsWith(_composerValue, StringComparison.OrdinalIgnoreCase))
                {
                    _composerGhostSuggestion = result.Text;
                    _composerGhostSuffix = result.Text[_composerValue.Length..];
                }
                else
                {
                    _composerGhostSuggestion = _composerValue + result.Text;
                    _composerGhostSuffix = result.Text;
                }

                UpdateActiveThreadCache();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task OnComposerKeyDown(KeyboardEventArgs args)
    {
        if (args.Key is "Tab" or "ArrowRight")
        {
            if (TryAcceptGhostSuggestion(_composerValue.Length, _composerValue.Length))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private bool TryAcceptGhostSuggestion(int selectionStart, int selectionEnd)
    {
        if (string.IsNullOrEmpty(_composerGhostSuffix))
        {
            return false;
        }

        if (selectionStart != _composerValue.Length || selectionEnd != _composerValue.Length)
        {
            return false;
        }

        _composerValue = string.Concat(_composerValue, _composerGhostSuffix);
        UpdateActiveThreadCache();
        _ = QueueComposerSuggestionAsync();
        return true;
    }

    private void RefreshComposerSuggestionFallback()
    {
        if (string.IsNullOrEmpty(_composerValue))
        {
            _composerGhostSuggestion = ComposerSuggestions[0];
            _composerGhostSuffix = _composerGhostSuggestion;
            return;
        }

        var suggestion = ComposerSuggestions.FirstOrDefault(candidate =>
            candidate.StartsWith(_composerValue, StringComparison.OrdinalIgnoreCase) &&
            candidate.Length > _composerValue.Length);

        _composerGhostSuggestion = suggestion ?? string.Empty;
        _composerGhostSuffix = _composerGhostSuggestion.Length == 0
            ? string.Empty
            : _composerGhostSuggestion[_composerValue.Length..];
        UpdateActiveThreadCache();
    }

    private void SetRecentTaskTargetCount(int viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            _recentTaskTargetCount = 5;
            return;
        }

        var estimated = (int)Math.Round((viewportHeight - 340) / 96d, MidpointRounding.AwayFromZero);
        _recentTaskTargetCount = Math.Clamp(estimated, 3, 10);
    }

    private bool ShouldAttachComposerBridge()
    {
        return _selectedRepository is not null;
    }

    private WorkspaceRepositoryListItem BuildRepositoryListItem(RepositoryDocument repository)
    {
        return new WorkspaceRepositoryListItem(
            Id: repository.Id,
            Name: repository.Name,
            BranchLabel: GetBranchLabel(repository),
            HealthLabel: GetRepositoryHealthLabel(repository),
            HealthColor: GetRepositoryHealthColor(repository),
            Progress: GetRepositoryProgress(repository),
            IsSelected: repository.Id == _selectedRepository?.Id);
    }

    private WorkspaceThreadState BuildThreadState(TaskDocument task)
    {
        var latestRun = GetLatestRun(task.Id);
        var cache = GetThreadCache(task.Id);
        var lastActivityUtc = latestRun?.CreatedAtUtc ?? task.CreatedAtUtc;
        if (cache is not null && cache.LastActivityUtc > lastActivityUtc)
        {
            lastActivityUtc = cache.LastActivityUtc;
        }

        var latestStateLabel = WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, latestRun);
        var latestStateColor = latestRun is null
            ? (task.Enabled ? Color.Default : Color.Secondary)
            : GetStateColor(latestRun.State);
        var latestRunHint = latestRun is null
            ? string.Empty
            : latestRun.CreatedAtUtc.ToLocalTime().ToString("g");

        return new WorkspaceThreadState(
            TaskId: task.Id,
            Title: task.Name,
            Harness: task.Harness,
            LatestStateLabel: latestStateLabel,
            LatestStateColor: latestStateColor,
            IsSelected: string.Equals(task.Id, _selectedTask?.Id, StringComparison.OrdinalIgnoreCase),
            HasUnread: cache?.HasUnreadActivity == true,
            LastActivityUtc: lastActivityUtc,
            LatestRunHint: latestRunHint);
    }

    private WorkspaceThreadUiCache? GetThreadCache(string taskId)
    {
        return _threadUiCacheByTaskId.TryGetValue(taskId, out var cache)
            ? cache
            : null;
    }

    private WorkspaceThreadUiCache GetOrCreateThreadCache(string taskId)
    {
        if (_threadUiCacheByTaskId.TryGetValue(taskId, out var existing))
        {
            return existing;
        }

        var created = new WorkspaceThreadUiCache();
        _threadUiCacheByTaskId[taskId] = created;
        return created;
    }

    private void PersistActiveThreadCache()
    {
        if (_selectedTask is null)
        {
            return;
        }

        var cache = GetOrCreateThreadCache(_selectedTask.Id);
        cache.ComposerDraft = _composerValue;
        cache.ComposerImages = _composerImages;
        cache.ComposerGhostSuggestion = _composerGhostSuggestion;
        cache.ComposerGhostSuffix = _composerGhostSuffix;
        cache.SelectedRunId = _selectedRun?.Id ?? cache.SelectedRunId;
        cache.LastActivityUtc = DateTime.UtcNow;
    }

    private void RestoreThreadDraft(string taskId)
    {
        var cache = GetOrCreateThreadCache(taskId);
        _composerValue = cache.ComposerDraft;
        _composerImages = cache.ComposerImages;
        _composerGhostSuggestion = cache.ComposerGhostSuggestion;
        _composerGhostSuffix = cache.ComposerGhostSuffix;
        cache.HasUnreadActivity = false;
    }

    private void UpdateActiveThreadCache()
    {
        if (_selectedTask is null)
        {
            return;
        }

        var cache = GetOrCreateThreadCache(_selectedTask.Id);
        cache.ComposerDraft = _composerValue;
        cache.ComposerImages = _composerImages;
        cache.ComposerGhostSuggestion = _composerGhostSuggestion;
        cache.ComposerGhostSuffix = _composerGhostSuffix;
        cache.SelectedRunId = _selectedRun?.Id ?? cache.SelectedRunId;
        cache.LastActivityUtc = DateTime.UtcNow;
    }

    private void SetThreadSelectedRun(string taskId, string runId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var cache = GetOrCreateThreadCache(taskId);
        cache.SelectedRunId = runId;
    }

    private void MarkThreadActivity(string taskId, DateTime timestampUtc, bool unread)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var cache = GetOrCreateThreadCache(taskId);
        if (timestampUtc > cache.LastActivityUtc)
        {
            cache.LastActivityUtc = timestampUtc;
        }

        if (unread)
        {
            cache.HasUnreadActivity = true;
        }
        else
        {
            cache.HasUnreadActivity = false;
        }
    }

    private void PruneThreadCaches()
    {
        if (_threadUiCacheByTaskId.Count == 0)
        {
            return;
        }

        var validTaskIds = _selectedRepositoryTasks
            .Select(task => task.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removeKeys = _threadUiCacheByTaskId.Keys
            .Where(taskId => !validTaskIds.Contains(taskId))
            .ToList();

        foreach (var taskId in removeKeys)
        {
            _threadUiCacheByTaskId.Remove(taskId);
        }
    }

    private void RebuildLatestRunsIndex()
    {
        _latestRunsByTask = _selectedRepositoryRuns
            .GroupBy(run => run.TaskId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(run => run.CreatedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);
    }

    private RunDocument? GetLatestRun(string taskId)
    {
        return _latestRunsByTask.TryGetValue(taskId, out var run)
            ? run
            : null;
    }

    private List<RunDocument> GetRunsForTask(string taskId)
    {
        return _selectedRepositoryRuns
            .Where(run => run.TaskId == taskId)
            .OrderByDescending(run => run.CreatedAtUtc)
            .ToList();
    }

    private bool MatchesTaskFilter(TaskDocument task)
    {
        var latestRun = GetLatestRun(task.Id);

        return _taskFilter switch
        {
            TaskFilterRunning => latestRun?.State is RunState.Running or RunState.Queued or RunState.PendingApproval,
            TaskFilterFailed => latestRun?.State is RunState.Failed,
            TaskFilterSucceeded => latestRun?.State is RunState.Succeeded,
            TaskFilterEnabled => task.Enabled,
            _ => true
        };
    }

    private bool MatchesHistoryRunState(RunDocument run)
    {
        if (string.Equals(_historyStateFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(_historyStateFilter, run.State.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesHistoryRunMode(RunDocument run)
    {
        if (string.Equals(_historyModeFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(_historyModeFilter, run.ExecutionMode.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesHistoryRunHarness(RunDocument run)
    {
        if (string.Equals(_historyHarnessFilter, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_selectedTask is not null && string.Equals(_selectedTask.Harness, _historyHarnessFilter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(run.StructuredProtocol, _historyHarnessFilter, StringComparison.OrdinalIgnoreCase)
               || run.StructuredProtocol.Contains(_historyHarnessFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttentionRepository(RepositoryDocument repository)
    {
        return !string.IsNullOrWhiteSpace(repository.LastSyncError)
               || repository.ModifiedCount > 0
               || repository.StagedCount > 0
               || repository.UntrackedCount > 0
               || repository.BehindCount > 0;
    }

    private string GetHistoryItemClass(RunDocument run)
    {
        return _selectedRun?.Id == run.Id
            ? "workspace-history-item workspace-history-item-selected"
            : "workspace-history-item";
    }

    private void ToggleHistoryPanel()
    {
        _historyPanelOpen = !_historyPanelOpen;
    }

    private static string GetBranchLabel(RepositoryDocument repository)
    {
        return string.IsNullOrWhiteSpace(repository.CurrentBranch)
            ? "Branch: -"
            : $"Branch: {repository.CurrentBranch}";
    }

    private static int GetWorkingTreeCount(RepositoryDocument repository)
    {
        return repository.StagedCount + repository.ModifiedCount + repository.UntrackedCount;
    }

    private static int GetRepositoryProgress(RepositoryDocument repository)
    {
        var debt = repository.StagedCount + repository.ModifiedCount + repository.UntrackedCount + repository.BehindCount + repository.AheadCount;
        var progress = Math.Max(0, 100 - Math.Min(100, debt * 8));
        if (!string.IsNullOrWhiteSpace(repository.LastSyncError))
        {
            progress = Math.Min(progress, 35);
        }

        return progress;
    }

    private static Color GetRepositoryHealthColor(RepositoryDocument repository)
    {
        if (!string.IsNullOrWhiteSpace(repository.LastSyncError))
        {
            return Color.Error;
        }

        if (GetWorkingTreeCount(repository) > 0 || repository.BehindCount > 0)
        {
            return Color.Warning;
        }

        if (repository.AheadCount > 0)
        {
            return Color.Info;
        }

        return Color.Success;
    }

    private static string GetRepositoryHealthLabel(RepositoryDocument repository)
    {
        if (!string.IsNullOrWhiteSpace(repository.LastSyncError))
        {
            return "Sync Error";
        }

        if (GetWorkingTreeCount(repository) > 0)
        {
            return "Dirty";
        }

        if (repository.BehindCount > 0)
        {
            return "Behind";
        }

        if (repository.AheadCount > 0)
        {
            return "Ahead";
        }

        return "Clean";
    }

    private int GetTaskProgressPercent(string taskId)
    {
        var runs = _selectedRepositoryRuns
            .Where(run => run.TaskId == taskId)
            .Take(10)
            .ToList();

        if (runs.Count == 0)
        {
            return 0;
        }

        var succeeded = runs.Count(run => run.State == RunState.Succeeded);
        return Math.Clamp((int)Math.Round(succeeded * 100d / runs.Count, MidpointRounding.AwayFromZero), 0, 100);
    }

    private Color GetTaskStateColor(TaskDocument task)
    {
        var latestRun = GetLatestRun(task.Id);
        if (latestRun is null)
        {
            return task.Enabled ? Color.Default : Color.Secondary;
        }

        return GetStateColor(latestRun.State);
    }

    private string GetTaskStateLabel(TaskDocument task)
    {
        var latestRun = GetLatestRun(task.Id);
        return WorkspaceStatusTextFormatter.FormatTaskStateLabel(task, latestRun);
    }

    private static Color GetStateColor(RunState state)
    {
        return state switch
        {
            RunState.Succeeded => Color.Success,
            RunState.Failed => Color.Error,
            RunState.Running => Color.Info,
            RunState.Queued => Color.Warning,
            RunState.PendingApproval => Color.Secondary,
            RunState.Cancelled => Color.Default,
            _ => Color.Default
        };
    }

    private static string GetRunSummary(RunDocument run)
    {
        if (!string.IsNullOrWhiteSpace(run.Summary))
        {
            return run.Summary;
        }

        return run.State switch
        {
            RunState.Running => "Execution in progress",
            RunState.Queued => "Queued",
            RunState.Failed => "Execution failed",
            RunState.Succeeded => "Execution succeeded",
            RunState.PendingApproval => "Pending approval",
            _ => "No summary"
        };
    }

    private string GetSelectedRunRawOutput()
    {
        if (_selectedRun is null)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(_selectedRun.OutputJson)
            ? "{ }"
            : _selectedRun.OutputJson;
    }

    private void ClearSelectedRunStructuredState()
    {
        _selectedRunStructuredEvents = [];
        _selectedRunDiffSnapshot = null;
        _selectedRunStructuredView = new RunStructuredViewSnapshot(string.Empty, 0, [], [], [], null, DateTime.UtcNow);
        _selectedRunQuestionRequests = [];
    }

    private string GetSelectedRunStructuredEventsJson()
    {
        if (_selectedRunStructuredEvents.Count == 0)
        {
            return "[]";
        }

        return JsonSerializer.Serialize(_selectedRunStructuredEvents, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private async Task CopySelectedRunStructuredEventsJsonAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", GetSelectedRunStructuredEventsJson());
            Snackbar.Add("Event JSON copied.", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Unable to copy event JSON: {ex.Message}", Severity.Warning);
        }
    }

    private string GetWorkspaceResolvedDiffPatch()
    {
        if (!string.IsNullOrWhiteSpace(_selectedRunDiffSnapshot?.DiffPatch))
        {
            return _selectedRunDiffSnapshot.DiffPatch;
        }

        return _selectedRunStructuredView.Diff?.DiffPatch ?? string.Empty;
    }

    private string GetWorkspaceResolvedDiffStat()
    {
        if (!string.IsNullOrWhiteSpace(_selectedRunDiffSnapshot?.DiffStat))
        {
            return _selectedRunDiffSnapshot.DiffStat;
        }

        return _selectedRunStructuredView.Diff?.DiffStat ?? string.Empty;
    }

    private static string MergePrompt(string currentPrompt, string composerText)
    {
        if (string.IsNullOrWhiteSpace(composerText))
        {
            return currentPrompt;
        }

        var suffix = $"Workspace instruction:\n{composerText}";
        if (string.IsNullOrWhiteSpace(currentPrompt))
        {
            return suffix;
        }

        return $"{currentPrompt.TrimEnd()}\n\n{suffix}";
    }

    private static string AppendCreateTaskPromptImageReferences(string prompt, IReadOnlyList<WorkspaceImageInput> images)
    {
        if (images.Count == 0)
        {
            return prompt;
        }

        var referenceBlock = BuildCreateTaskImageReferenceBlock(images);
        if (string.IsNullOrWhiteSpace(referenceBlock))
        {
            return prompt;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return referenceBlock;
        }

        return $"{prompt.TrimEnd()}\n\n{referenceBlock}";
    }

    private static string BuildCreateTaskImageReferenceBlock(IReadOnlyList<WorkspaceImageInput> images)
    {
        if (images.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Attached images for context:");

        for (var index = 0; index < images.Count; index++)
        {
            var image = images[index];
            var dimensions = image.Width is > 0 && image.Height is > 0
                ? $", {image.Width}x{image.Height}"
                : string.Empty;
            var sizeKb = Math.Max(1, (int)Math.Round(image.SizeBytes / 1024d));

            builder.Append("- Image ")
                .Append(index + 1)
                .Append(": ")
                .Append(image.FileName)
                .Append(" (")
                .Append(image.MimeType)
                .Append(dimensions)
                .Append(", ")
                .Append(sizeKb)
                .Append(" KB)")
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static int GetPromptHistoryImageCount(WorkspacePromptEntryDocument entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ImageMetadataJson))
        {
            return entry.HasImages ? 1 : 0;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.ImageMetadataJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return entry.HasImages ? 1 : 0;
            }

            return document.RootElement.GetArrayLength();
        }
        catch
        {
            return entry.HasImages ? 1 : 0;
        }
    }

    private static UpdateTaskRequest ToUpdateRequest(TaskDocument task, string prompt)
    {
        return new UpdateTaskRequest(
            task.Name,
            task.Harness,
            prompt,
            task.Command,
            task.AutoCreatePullRequest,
            task.Enabled,
            task.RetryPolicy,
            task.Timeouts,
            task.SandboxProfile,
            task.ArtifactPolicy,
            task.ApprovalProfile,
            task.ConcurrencyLimit,
            task.InstructionFiles.Count > 0 ? [.. task.InstructionFiles] : null,
            task.ArtifactPatterns.Count > 0 ? [.. task.ArtifactPatterns] : null,
            task.LinkedFailureRuns.Count > 0 ? [.. task.LinkedFailureRuns] : null,
            task.ExecutionModeDefault);
    }

    private static string BuildFallbackTaskName(string prompt)
    {
        var firstLine = prompt
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        var normalized = string.Join(
            " ",
            firstLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        if (normalized.Length == 0)
        {
            return $"Task {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }

        return normalized.Length <= 80
            ? normalized
            : normalized[..80].TrimEnd();
    }

    private static string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }

    public async ValueTask DisposeAsync()
    {
        _selectionSubscription?.Dispose();
        _runLogSubscription?.Dispose();
        _runStatusSubscription?.Dispose();
        _structuredSubscription?.Dispose();
        _diffSubscription?.Dispose();
        _toolSubscription?.Dispose();
        _composerSuggestionCts?.Cancel();
        _composerSuggestionCts?.Dispose();

        if (_workspaceJsModule is not null)
        {
            try
            {
                if (_viewportListenerHandle is not null)
                {
                    await _workspaceJsModule.InvokeVoidAsync("unregisterViewportListener", _viewportListenerHandle);
                }
            }
            catch
            {
            }

            try
            {
                if (_composerKeyBridgeHandle is not null)
                {
                    await _workspaceJsModule.InvokeVoidAsync("unregisterComposerKeyBridge", _composerKeyBridgeHandle);
                }
            }
            catch
            {
            }

            try
            {
                await _workspaceJsModule.DisposeAsync();
            }
            catch
            {
            }
        }

        _dotNetRef?.Dispose();
    }

    private enum PromptDraftMode
    {
        Generate,
        Improve
    }

}
