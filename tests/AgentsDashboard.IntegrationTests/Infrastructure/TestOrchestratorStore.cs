using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AgentsDashboard.IntegrationTests.Infrastructure;

public static class TestOrchestratorStore
{
    public static OrchestratorStore Create(string connectionString, string? databaseName = null)
    {
        var dbName = databaseName ?? $"test_{Guid.NewGuid():N}";
        var options = Options.Create(new OrchestratorOptions
        {
            MongoConnectionString = connectionString,
            MongoDatabase = dbName,
        });

        var client = new MongoClient(connectionString);
        return new OrchestratorStore(client, options);
    }
}
