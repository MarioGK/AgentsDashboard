using System.Collections;
using System.Linq.Expressions;
using System.Text.Json;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class TrackedRepositorySet<T>(IRepository<T> repository) : IEnumerable<T>, ITrackedRepositorySet
    where T : class
{
    private readonly object _sync = new();
    private List<T>? _items;
    private readonly HashSet<string> _trackedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _addedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletedKeys = new(StringComparer.Ordinal);

    public IQueryable<T> AsNoTracking()
    {
        EnsureLoadedSync();
        var snapshot = GetSnapshot(track: false);
        return snapshot.AsQueryable();
    }

    public void Add(T entity)
    {
        repository.EnsureDocumentId(entity);
        EnsureLoadedSync();

        var id = repository.GetDocumentId(entity);
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Document id is required for type '{typeof(T).Name}'.");
        }

        lock (_sync)
        {
            _items!.RemoveAll(x => string.Equals(repository.GetDocumentId(x), id, StringComparison.Ordinal));
            _items.Add(entity);
            _trackedKeys.Add(id);
            _addedKeys.Add(id);
            _deletedKeys.Remove(id);
        }
    }

    public void Remove(T entity)
    {
        EnsureLoadedSync();

        var id = repository.GetDocumentId(entity);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        lock (_sync)
        {
            _items!.RemoveAll(x => string.Equals(repository.GetDocumentId(x), id, StringComparison.Ordinal));
            _trackedKeys.Remove(id);

            if (_addedKeys.Contains(id))
            {
                _addedKeys.Remove(id);
            }
            else
            {
                _deletedKeys.Add(id);
            }
        }
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var compiled = predicate.Compile();

        lock (_sync)
        {
            var item = _items!.FirstOrDefault(compiled);
            if (item is not null)
            {
                Track(item);
            }

            return item;
        }
    }

    public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var compiled = predicate.Compile();

        lock (_sync)
        {
            return _items!.Any(compiled);
        }
    }

    public async Task<long> LongCountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var compiled = predicate.Compile();

        lock (_sync)
        {
            return _items!.LongCount(compiled);
        }
    }

    public async Task<int> DeleteWhereAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        await EnsureLoadedAsync(cancellationToken);
        var compiled = predicate.Compile();

        lock (_sync)
        {
            var matches = _items!.Where(compiled).ToList();
            if (matches.Count == 0)
            {
                return 0;
            }

            foreach (var match in matches)
            {
                var id = repository.GetDocumentId(match);
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                _items!.RemoveAll(x => string.Equals(repository.GetDocumentId(x), id, StringComparison.Ordinal));
                _trackedKeys.Remove(id);
                if (_addedKeys.Contains(id))
                {
                    _addedKeys.Remove(id);
                }
                else
                {
                    _deletedKeys.Add(id);
                }
            }

            return matches.Count;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        List<T> itemsSnapshot;
        List<string> trackedKeysSnapshot;
        List<string> deletedKeysSnapshot;

        lock (_sync)
        {
            if (_items is null)
            {
                return;
            }

            itemsSnapshot = _items.ToList();
            trackedKeysSnapshot = _trackedKeys.ToList();
            deletedKeysSnapshot = _deletedKeys.ToList();
        }

        foreach (var deletedId in deletedKeysSnapshot)
        {
            await repository.DeleteByIdAsync(deletedId, cancellationToken);
        }

        var itemsById = itemsSnapshot
            .Select(item => (Id: repository.GetDocumentId(item), Item: item))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .GroupBy(entry => entry.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Item, StringComparer.Ordinal);

        foreach (var trackedId in trackedKeysSnapshot)
        {
            if (deletedKeysSnapshot.Contains(trackedId, StringComparer.Ordinal))
            {
                continue;
            }

            if (!itemsById.TryGetValue(trackedId, out var entity))
            {
                continue;
            }

            await repository.UpsertAsync(entity, cancellationToken);
        }

        lock (_sync)
        {
            _trackedKeys.Clear();
            _addedKeys.Clear();
            _deletedKeys.Clear();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        EnsureLoadedSync();

        lock (_sync)
        {
            foreach (var item in _items!)
            {
                Track(item);
            }

            return _items.ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void Track(T item)
    {
        var id = repository.GetDocumentId(item);
        if (!string.IsNullOrWhiteSpace(id))
        {
            _trackedKeys.Add(id);
        }
    }

    private List<T> GetSnapshot(bool track)
    {
        lock (_sync)
        {
            var snapshot = _items!.Select(Clone).ToList();
            if (track)
            {
                foreach (var item in snapshot)
                {
                    Track(item);
                }
            }

            return snapshot;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_items is not null)
        {
            return;
        }

        var loaded = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _items ??= loaded;
        }
    }

    private void EnsureLoadedSync()
    {
        if (_items is not null)
        {
            return;
        }

        EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static T Clone(T value)
    {
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!;
    }
}
