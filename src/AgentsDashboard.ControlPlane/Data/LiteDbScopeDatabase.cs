namespace AgentsDashboard.ControlPlane.Data;

public sealed class LiteDbScopeDatabase(
    LiteDbExecutor executor,
    LiteDbDatabase database)
{
    public string DatabasePath => database.DatabasePath;

    public Task<LiteDbScopeTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LiteDbScopeTransaction());
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
