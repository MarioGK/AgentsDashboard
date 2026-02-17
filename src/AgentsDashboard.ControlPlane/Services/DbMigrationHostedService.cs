using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DbMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<DbMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            logger.ZLogInformation("EF Core migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Failed to apply EF Core migrations");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
