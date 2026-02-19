using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using LiteDB;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class LiteDbRepository<T>(
    LiteDbExecutor executor,
    ILiteDbCollectionNameResolver collectionNameResolver) : IRepository<T>
    where T : class
{
    private readonly LiteDbCollectionDefinition _definition = collectionNameResolver.Resolve<T>();
    private readonly PropertyInfo _idProperty =
        typeof(T).GetProperty(collectionNameResolver.Resolve<T>().IdPropertyName, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Type '{typeof(T).FullName}' does not define id property '{collectionNameResolver.Resolve<T>().IdPropertyName}'.");

    public string GetDocumentId(T document)
    {
        var value = _idProperty.GetValue(document);
        return value?.ToString() ?? string.Empty;
    }

    public void EnsureDocumentId(T document)
    {
        if (!string.Equals(_idProperty.Name, "Id", StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(GetDocumentId(document)))
        {
            return;
        }

        _idProperty.SetValue(document, Guid.NewGuid().ToString("N"));
    }

    public Task<List<T>> ListAsync(CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => GetCollection(db).FindAll().Select(Clone).ToList(),
            cancellationToken);
    }

    public Task<T?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.FromResult<T?>(null);
        }

        return executor.ExecuteAsync(
            db => CloneOrDefault(GetCollection(db).FindById(new BsonValue(id))),
            cancellationToken);
    }

    public Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => GetCollection(db).Find(predicate).Select(Clone).FirstOrDefault(),
            cancellationToken);
    }

    public Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => GetCollection(db).Find(predicate).Select(Clone).ToList(),
            cancellationToken);
    }

    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => GetCollection(db).Exists(predicate),
            cancellationToken);
    }

    public Task<long> LongCountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => (long)GetCollection(db).Count(predicate),
            cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(Func<IQueryable<T>, TResult> query, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => query(GetCollection(db).FindAll().Select(Clone).AsQueryable()),
            cancellationToken);
    }

    public Task InsertAsync(T document, CancellationToken cancellationToken)
    {
        EnsureDocumentId(document);
        var copy = Clone(document);
        return executor.ExecuteAsync(
            db =>
            {
                GetCollection(db).Insert(copy);
                return 0;
            },
            cancellationToken);
    }

    public Task UpsertAsync(T document, CancellationToken cancellationToken)
    {
        EnsureDocumentId(document);
        var copy = Clone(document);
        return executor.ExecuteAsync(
            db =>
            {
                GetCollection(db).Upsert(copy);
                return 0;
            },
            cancellationToken);
    }

    public Task DeleteByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Task.CompletedTask;
        }

        return executor.ExecuteAsync(
            db =>
            {
                _ = GetCollection(db).Delete(new BsonValue(id));
                return 0;
            },
            cancellationToken);
    }

    public Task<int> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db => GetCollection(db).DeleteMany(predicate),
            cancellationToken);
    }

    public Task EnsureIndexAsync<K>(Expression<Func<T, K>> keySelector, bool unique, CancellationToken cancellationToken)
    {
        return executor.ExecuteAsync(
            db =>
            {
                _ = GetCollection(db).EnsureIndex(keySelector, unique);
                return 0;
            },
            cancellationToken);
    }

    private ILiteCollection<T> GetCollection(LiteDatabase database)
    {
        return database.GetCollection<T>(_definition.CollectionName);
    }

    private static T Clone(T value)
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(
            System.Text.Json.JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            (JsonSerializerOptions?)null)!;
    }

    private static T? CloneOrDefault(T? value)
    {
        return value is null ? null : Clone(value);
    }
}
