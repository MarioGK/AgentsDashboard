using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AgentsDashboard.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
[LongRunJob]
public class MongoOperationsBenchmarks
{
    private const string ConnectionString = "mongodb://localhost:27017";
    private const string DatabaseName = "benchmark_db";

    private MongoClient _client = null!;
    private OrchestratorStore _store = null!;
    private ProjectDocument _project = null!;
    private RepositoryDocument _repo = null!;
    private TaskDocument _task = null!;

    [Params(10, 100, 500)]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _client = new MongoClient(ConnectionString);
        var options = Options.Create(new OrchestratorOptions
        {
            MongoConnectionString = ConnectionString,
            MongoDatabase = DatabaseName,
        });

        _store = new OrchestratorStore(_client, options);
        await _store.InitializeAsync(CancellationToken.None);

        _project = await _store.CreateProjectAsync(
            new CreateProjectRequest("Benchmark Project", "For benchmarking"),
            CancellationToken.None);

        _repo = await _store.CreateRepositoryAsync(
            new CreateRepositoryRequest(_project.Id, "benchmark-repo", "https://github.com/test/benchmark.git", "main"),
            CancellationToken.None);

        _task = await _store.CreateTaskAsync(
            new CreateTaskRequest(
                _repo.Id,
                "Benchmark Task",
                TaskKind.OneShot,
                "codex",
                "Test prompt",
                "echo test",
                false,
                "",
                true),
            CancellationToken.None);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _client.DropDatabase(DatabaseName);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        var db = _client.GetDatabase(DatabaseName);
        db.GetCollection<RunDocument>("runs").DeleteMany(Builders<RunDocument>.Filter.Empty);
    }

    [Benchmark(Description = "Create single run")]
    public async Task<RunDocument> CreateSingleRun()
    {
        return await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
    }

    [Benchmark(Description = "Create multiple runs sequentially")]
    public async Task CreateMultipleRunsSequential()
    {
        for (int i = 0; i < DocumentCount; i++)
        {
            await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
        }
    }

    [Benchmark(Description = "Create multiple runs concurrently")]
    public async Task CreateMultipleRunsConcurrent()
    {
        var tasks = Enumerable.Range(0, DocumentCount)
            .Select(_ => _store.CreateRunAsync(_task, _project.Id, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Mark run started")]
    public async Task MarkRunStarted()
    {
        var run = await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
        await _store.MarkRunStartedAsync(run.Id, CancellationToken.None);
    }

    [Benchmark(Description = "Mark run completed")]
    public async Task MarkRunCompleted()
    {
        var run = await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
        await _store.MarkRunCompletedAsync(run.Id, true, "Success", "{}", CancellationToken.None);
    }

    [Benchmark(Description = "Count active runs")]
    public async Task<long> CountActiveRuns()
    {
        return await _store.CountActiveRunsAsync(CancellationToken.None);
    }

    [Benchmark(Description = "List recent runs")]
    public async Task<List<RunDocument>> ListRecentRuns()
    {
        return await _store.ListRecentRunsAsync(CancellationToken.None);
    }

    [Benchmark(Description = "Full run lifecycle")]
    public async Task FullRunLifecycle()
    {
        var run = await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
        await _store.MarkRunStartedAsync(run.Id, CancellationToken.None);
        await _store.MarkRunCompletedAsync(run.Id, true, "Success", "{}", CancellationToken.None);
    }

    [Benchmark(Description = "Add run log")]
    public async Task AddRunLog()
    {
        var run = await _store.CreateRunAsync(_task, _project.Id, CancellationToken.None);
        var logEvent = new RunLogEvent
        {
            RunId = run.Id,
            Level = "info",
            Message = "Benchmark log message"
        };
        await _store.AddRunLogAsync(logEvent, CancellationToken.None);
    }

    [Benchmark(Description = "List run logs")]
    public async Task<List<RunLogEvent>> ListRunLogs()
    {
        return await _store.ListRunLogsAsync(_task.Id, CancellationToken.None);
    }
}
