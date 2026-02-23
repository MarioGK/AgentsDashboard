
using LiteDB;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class LiteDbDatabase(IOptions<OrchestratorOptions> orchestratorOptions) : IHostedService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath = ResolveDatabasePath(orchestratorOptions.Value.LiteDbPath);
    private LiteDatabase? _database;
    private bool _stopped;

    public string DatabasePath => _databasePath;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_database is null)
            {
                EnsureDirectoryExists(_databasePath);
                _database = CreateDatabase(_databasePath);
            }

            _stopped = false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            _stopped = true;
            _database?.Dispose();
            _database = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<LiteDatabase, TResult> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_stopped)
            {
                throw new ObjectDisposedException(nameof(LiteDbDatabase));
            }

            _database ??= CreateDatabase(_databasePath);
            return operation(_database);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _gate.Dispose();
    }

    private static LiteDatabase CreateDatabase(string databasePath)
    {
        var connectionString = new ConnectionString
        {
            Filename = databasePath,
            Connection = ConnectionType.Shared,
            Upgrade = true
        };

        return new LiteDatabase(connectionString);
    }

    private static void EnsureDirectoryExists(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
    }

    private static string ResolveDatabasePath(string liteDbPath)
    {
        return RepositoryPathResolver.ResolveDataPath(
            liteDbPath,
            OrchestratorOptions.DefaultLiteDbPath);
    }
}
