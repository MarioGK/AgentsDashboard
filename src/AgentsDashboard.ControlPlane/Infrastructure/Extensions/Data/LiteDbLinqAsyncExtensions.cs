namespace AgentsDashboard.ControlPlane.Infrastructure.Extensions.Data;

public static class LiteDbLinqAsyncExtensions
{
    public static Task<List<T>> ToListAsync<T>(this IEnumerable<T> source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source.ToList());
    }

    public static Task<T?> FirstOrDefaultAsync<T>(this IEnumerable<T> source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source.FirstOrDefault());
    }

    public static Task<T?> FirstOrDefaultAsync<T>(this IEnumerable<T> source, Func<T, bool> predicate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source.FirstOrDefault(predicate));
    }

    public static Task<bool> AnyAsync<T>(this IEnumerable<T> source, Func<T, bool> predicate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source.Any(predicate));
    }

    public static Task<long> LongCountAsync<T>(this IEnumerable<T> source, Func<T, bool> predicate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(source.LongCount(predicate));
    }
}
