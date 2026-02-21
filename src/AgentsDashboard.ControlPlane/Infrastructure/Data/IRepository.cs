using System.Linq.Expressions;

namespace AgentsDashboard.ControlPlane.Data;

public interface IRepository<T>
    where T : class
{
    string GetDocumentId(T document);
    void EnsureDocumentId(T document);
    Task<List<T>> ListAsync(CancellationToken cancellationToken);
    Task<T?> FindByIdAsync(string id, CancellationToken cancellationToken);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);
    Task<bool> AnyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);
    Task<long> LongCountAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);
    Task<TResult> QueryAsync<TResult>(Func<IQueryable<T>, TResult> query, CancellationToken cancellationToken);
    Task InsertAsync(T document, CancellationToken cancellationToken);
    Task UpsertAsync(T document, CancellationToken cancellationToken);
    Task DeleteByIdAsync(string id, CancellationToken cancellationToken);
    Task<int> DeleteManyAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);
    Task EnsureIndexAsync<K>(Expression<Func<T, K>> keySelector, bool unique, CancellationToken cancellationToken);
}
