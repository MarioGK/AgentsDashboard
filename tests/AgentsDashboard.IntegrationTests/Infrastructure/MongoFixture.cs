using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentsDashboard.IntegrationTests.Infrastructure;

public sealed class SqliteFixture : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agentsdashboard-integration-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={_databasePath}";

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(ConnectionString)
            .Options;

        await using var dbContext = new OrchestratorDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}

[CollectionDefinition("Sqlite")]
public class SqliteCollection : ICollectionFixture<SqliteFixture>;
