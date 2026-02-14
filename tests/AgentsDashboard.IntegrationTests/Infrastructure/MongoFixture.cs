using Testcontainers.MongoDb;

namespace AgentsDashboard.IntegrationTests.Infrastructure;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:8.0")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition("Mongo")]
public class MongoCollection : ICollectionFixture<MongoFixture>;
