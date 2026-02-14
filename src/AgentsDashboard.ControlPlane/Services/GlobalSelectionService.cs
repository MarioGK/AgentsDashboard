using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.JSInterop;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class SelectionChangedEventArgs
{
    public string? ProjectId { get; init; }
    public string? RepositoryId { get; init; }
    public ProjectDocument? Project { get; init; }
    public RepositoryDocument? Repository { get; init; }
}

public interface IGlobalSelectionService
{
    string? SelectedProjectId { get; }
    string? SelectedRepositoryId { get; }
    ProjectDocument? SelectedProject { get; }
    RepositoryDocument? SelectedRepository { get; }
    IReadOnlyList<ProjectDocument> Projects { get; }
    IReadOnlyList<RepositoryDocument> Repositories { get; }
    bool IsInitialized { get; }

    event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    Task InitializeAsync(CancellationToken cancellationToken);
    Task SelectProjectAsync(string? projectId, CancellationToken cancellationToken);
    Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
    IDisposable Subscribe(Action<SelectionChangedEventArgs> handler);
}

public sealed class GlobalSelectionService : IGlobalSelectionService, IDisposable
{
    private readonly OrchestratorStore _store;
    private readonly IJSRuntime _jsRuntime;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<Action<SelectionChangedEventArgs>> _subscribers = [];
    private readonly object _subscribersLock = new();
    private bool _initialized;
    private bool _disposed;

    public string? SelectedProjectId { get; private set; }
    public string? SelectedRepositoryId { get; private set; }
    public ProjectDocument? SelectedProject { get; private set; }
    public RepositoryDocument? SelectedRepository { get; private set; }
    public List<ProjectDocument> ProjectList { get; private set; } = [];
    public List<RepositoryDocument> RepositoryList { get; private set; } = [];
    public IReadOnlyList<ProjectDocument> Projects => ProjectList;
    public IReadOnlyList<RepositoryDocument> Repositories => RepositoryList;
    public bool IsInitialized => _initialized;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public GlobalSelectionService(OrchestratorStore store, IJSRuntime jsRuntime)
    {
        _store = store;
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            ProjectList = await _store.ListProjectsAsync(cancellationToken);

            var savedProjectId = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "selectedProjectId");
            var savedRepoId = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "selectedRepositoryId");

            if (savedProjectId is not null && ProjectList.Any(p => p.Id == savedProjectId))
            {
                await SetProjectInternalAsync(savedProjectId, savedRepoId, cancellationToken);
            }
            else if (ProjectList.Count > 0)
            {
                await SetProjectInternalAsync(ProjectList[0].Id, null, cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SelectProjectAsync(string? projectId, CancellationToken cancellationToken)
    {
        if (projectId == SelectedProjectId) return;

        if (projectId is not null)
        {
            await SetProjectInternalAsync(projectId, null, cancellationToken);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedProjectId", projectId);
        }
        else
        {
            SelectedProjectId = null;
            SelectedProject = null;
            SelectedRepositoryId = null;
            SelectedRepository = null;
            RepositoryList = [];
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedProjectId", string.Empty);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", string.Empty);
            RaiseSelectionChanged();
        }
    }

    public async Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken)
    {
        if (repositoryId == SelectedRepositoryId) return;

        SelectedRepositoryId = repositoryId;
        SelectedRepository = repositoryId is not null
            ? RepositoryList.FirstOrDefault(r => r.Id == repositoryId)
            : null;

        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", repositoryId ?? string.Empty);
        RaiseSelectionChanged();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ProjectList = await _store.ListProjectsAsync(cancellationToken);

        if (SelectedProjectId is not null)
        {
            RepositoryList = await _store.ListRepositoriesAsync(SelectedProjectId, cancellationToken);
            SelectedProject = ProjectList.FirstOrDefault(p => p.Id == SelectedProjectId);
            SelectedRepository = SelectedRepositoryId is not null
                ? RepositoryList.FirstOrDefault(r => r.Id == SelectedRepositoryId)
                : null;
        }
    }

    public IDisposable Subscribe(Action<SelectionChangedEventArgs> handler)
    {
        lock (_subscribersLock)
        {
            _subscribers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(handler);
            }
        });
    }

    private async Task SetProjectInternalAsync(string projectId, string? preferredRepoId, CancellationToken cancellationToken)
    {
        SelectedProjectId = projectId;
        SelectedProject = ProjectList.FirstOrDefault(p => p.Id == projectId);
        RepositoryList = await _store.ListRepositoriesAsync(projectId, cancellationToken);

        if (preferredRepoId is not null && RepositoryList.Any(r => r.Id == preferredRepoId))
        {
            SelectedRepositoryId = preferredRepoId;
            SelectedRepository = RepositoryList.First(r => r.Id == preferredRepoId);
        }
        else if (RepositoryList.Count > 0)
        {
            SelectedRepositoryId = RepositoryList[0].Id;
            SelectedRepository = RepositoryList[0];
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "selectedRepositoryId", SelectedRepositoryId);
        }
        else
        {
            SelectedRepositoryId = null;
            SelectedRepository = null;
        }

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        var args = new SelectionChangedEventArgs
        {
            ProjectId = SelectedProjectId,
            RepositoryId = SelectedRepositoryId,
            Project = SelectedProject,
            Repository = SelectedRepository
        };

        SelectionChanged?.Invoke(this, args);

        List<Action<SelectionChangedEventArgs>> handlers;
        lock (_subscribersLock)
        {
            handlers = [.. _subscribers];
        }

        foreach (var handler in handlers)
        {
            handler(args);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initLock.Dispose();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public Subscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _dispose();
        }
    }
}
