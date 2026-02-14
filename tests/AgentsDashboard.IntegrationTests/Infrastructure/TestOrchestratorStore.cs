using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AgentsDashboard.IntegrationTests.Infrastructure;

public static class TestOrchestratorStore
{
    public static OrchestratorStore Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(connectionString)
            .Options;

        var dbContextFactory = new StaticDbContextFactory(options);
        return new OrchestratorStore(dbContextFactory);
    }

    private sealed class StaticDbContextFactory(DbContextOptions<OrchestratorDbContext> options) : IDbContextFactory<OrchestratorDbContext>
    {
        public OrchestratorDbContext CreateDbContext() => new(options);

        public Task<OrchestratorDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new OrchestratorDbContext(options));
    }
}
