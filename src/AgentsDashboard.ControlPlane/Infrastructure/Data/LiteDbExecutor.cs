using LiteDB;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class LiteDbExecutor(LiteDbDatabase database)
{
    public Task<TResult> ExecuteAsync<TResult>(Func<LiteDatabase, TResult> operation, CancellationToken cancellationToken)
    {
        return database.ExecuteAsync(operation, cancellationToken);
    }

    public Task ExecuteAsync(Action<LiteDatabase> operation, CancellationToken cancellationToken)
    {
        return database.ExecuteAsync(
            db =>
            {
                operation(db);
                return 0;
            },
            cancellationToken);
    }
}
