namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class LiteDbExecutionDatabase(
    LiteDbExecutor executor,
    LiteDbDatabase database)
{
    public string DatabasePath => database.DatabasePath;

    public Task<LiteDbExecutionTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LiteDbExecutionTransaction());
    }

    public async Task ExecuteSqlRawAsync(string sql, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        if (sql.Contains("VACUUM", StringComparison.OrdinalIgnoreCase))
        {
            await executor.ExecuteAsync(
                db =>
                {
                    _ = db.Rebuild();
                },
                cancellationToken);
        }
    }
}
