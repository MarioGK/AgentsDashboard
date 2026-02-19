using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.JSInterop;

namespace AgentsDashboard.ControlPlane.Services;

public interface ILocalStorageService
{
    Task<string?> GetItemAsync(string key);
    Task SetItemAsync(string key, string value);
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

public sealed class LocalStorageService(IJSRuntime jsRuntime) : ILocalStorageService
{
    public Task<string?> GetItemAsync(string key)
        => jsRuntime.InvokeAsync<string?>("localStorage.getItem", key).AsTask();

    public Task SetItemAsync(string key, string value)
        => jsRuntime.InvokeVoidAsync("localStorage.setItem", key, value).AsTask();
}
