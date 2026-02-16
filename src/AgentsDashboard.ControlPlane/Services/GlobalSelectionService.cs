using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.JSInterop;

namespace AgentsDashboard.ControlPlane.Services;

public interface ILocalStorageService
{
    Task<string?> GetItemAsync(string key);
    Task SetItemAsync(string key, string value);
}

public sealed class LocalStorageService(IJSRuntime jsRuntime) : ILocalStorageService
{
    public Task<string?> GetItemAsync(string key)
        => jsRuntime.InvokeAsync<string?>("localStorage.getItem", key).AsTask();

    public Task SetItemAsync(string key, string value)
        => jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value).AsTask();
}

public sealed class SelectionChangedEventArgs
{
    public string? RepositoryId { get; init; }
    public RepositoryDocument? Repository { get; init; }
}

public interface IGlobalSelectionService
{
    string? SelectedRepositoryId { get; }
    RepositoryDocument? SelectedRepository { get; }
    IReadOnlyList<RepositoryDocument> Repositories { get; }
    bool IsInitialized { get; }

    event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    Task InitializeAsync(CancellationToken cancellationToken);
    Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken);
    Task RefreshAsync(CancellationToken cancellationToken);
    IDisposable Subscribe(Action<SelectionChangedEventArgs> handler);
}

public sealed class GlobalSelectionService(IOrchestratorStore store, ILocalStorageService localStorage) : IGlobalSelectionService, IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<Action<SelectionChangedEventArgs>> _subscribers = [];
    private readonly object _subscribersLock = new();
    private bool _initialized;
    private bool _disposed;

    public string? SelectedRepositoryId { get; private set; }
    public RepositoryDocument? SelectedRepository { get; private set; }
    public List<RepositoryDocument> RepositoryList { get; private set; } = [];
    public IReadOnlyList<RepositoryDocument> Repositories => RepositoryList;
    public bool IsInitialized => _initialized;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            RepositoryList = await store.ListRepositoriesAsync(cancellationToken);

            var savedRepoId = await localStorage.GetItemAsync("selectedRepositoryId");
            if (!string.IsNullOrWhiteSpace(savedRepoId) && RepositoryList.Any(r => r.Id == savedRepoId))
            {
                SelectedRepositoryId = savedRepoId;
                SelectedRepository = RepositoryList.First(r => r.Id == savedRepoId);
            }
            else if (RepositoryList.Count > 0)
            {
                SelectedRepositoryId = RepositoryList[0].Id;
                SelectedRepository = RepositoryList[0];
                await localStorage.SetItemAsync("selectedRepositoryId", SelectedRepositoryId);
            }

            _initialized = true;
            RaiseSelectionChanged();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SelectRepositoryAsync(string? repositoryId, CancellationToken cancellationToken)
    {
        if (repositoryId == SelectedRepositoryId)
        {
            return;
        }

        SelectedRepositoryId = repositoryId;
        SelectedRepository = repositoryId is not null
            ? RepositoryList.FirstOrDefault(r => r.Id == repositoryId)
            : null;

        await localStorage.SetItemAsync("selectedRepositoryId", repositoryId ?? string.Empty);
        RaiseSelectionChanged();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        RepositoryList = await store.ListRepositoriesAsync(cancellationToken);
        SelectedRepository = SelectedRepositoryId is not null
            ? RepositoryList.FirstOrDefault(r => r.Id == SelectedRepositoryId)
            : null;

        if (SelectedRepository is null && RepositoryList.Count > 0)
        {
            SelectedRepository = RepositoryList[0];
            SelectedRepositoryId = SelectedRepository.Id;
            await localStorage.SetItemAsync("selectedRepositoryId", SelectedRepositoryId);
        }

        RaiseSelectionChanged();
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

    private void RaiseSelectionChanged()
    {
        var args = new SelectionChangedEventArgs
        {
            RepositoryId = SelectedRepositoryId,
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initLock.Dispose();
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dispose();
        }
    }
}
