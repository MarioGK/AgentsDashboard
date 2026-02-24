using LiteDB;

namespace AgentsDashboard.TaskRuntime.Infrastructure.Data;

public sealed class TaskRuntimeLiteDbStore(TaskRuntimeOptions options) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly LiteDatabase _database = new(ResolveConnectionString(options.LiteDbPath));

    public async Task<T> ExecuteAsync<T>(Func<LiteDatabase, T> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return operation(_database);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExecuteAsync(Action<LiteDatabase> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            operation(_database);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _database.Dispose();
        _gate.Dispose();
    }

    private static string ResolveConnectionString(string configuredPath)
    {
        if (string.Equals(configuredPath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return configuredPath;
        }

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return TaskRuntimeOptions.DefaultLiteDbPath;
        }

        return configuredPath.Trim();
    }
}
