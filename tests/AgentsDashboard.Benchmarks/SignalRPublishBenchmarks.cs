using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class SignalRPublishBenchmarks
{
    private SignalRRunEventPublisher _publisher = null!;
    private List<RunDocument> _runs = null!;
    private List<RunLogEvent> _logEvents = null!;

    [Params(10, 100, 1000)]
    public int EventCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var mockHubContext = new MockHubContext();
        _publisher = new SignalRRunEventPublisher(mockHubContext);

        _runs = new List<RunDocument>();
        _logEvents = new List<RunLogEvent>();

        for (int i = 0; i < EventCount; i++)
        {
            _runs.Add(new RunDocument
            {
                Id = $"run-{i}",
                State = i % 2 == 0 ? RunState.Running : RunState.Succeeded,
                Summary = $"Run {i} summary",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                EndedAtUtc = i % 2 == 0 ? null : DateTime.UtcNow
            });

            _logEvents.Add(new RunLogEvent
            {
                RunId = $"run-{i % 10}",
                Level = i % 5 == 0 ? "error" : "info",
                Message = $"Log message {i} with some content to simulate realistic log entries",
                TimestampUtc = DateTime.UtcNow
            });
        }
    }

    [Benchmark(Description = "Publish status updates sequentially")]
    public async Task PublishStatusSequential()
    {
        foreach (var run in _runs)
        {
            await _publisher.PublishStatusAsync(run, CancellationToken.None);
        }
    }

    [Benchmark(Description = "Publish status updates concurrently")]
    public async Task PublishStatusConcurrent()
    {
        var tasks = _runs.Select(run => _publisher.PublishStatusAsync(run, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Publish log events sequentially")]
    public async Task PublishLogsSequential()
    {
        foreach (var logEvent in _logEvents)
        {
            await _publisher.PublishLogAsync(logEvent, CancellationToken.None);
        }
    }

    [Benchmark(Description = "Publish log events concurrently")]
    public async Task PublishLogsConcurrent()
    {
        var tasks = _logEvents.Select(log => _publisher.PublishLogAsync(log, CancellationToken.None));
        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Mixed status and log publishing")]
    public async Task PublishMixed()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < EventCount; i++)
        {
            if (i % 2 == 0)
                tasks.Add(_publisher.PublishStatusAsync(_runs[i], CancellationToken.None));
            else
                tasks.Add(_publisher.PublishLogAsync(_logEvents[i], CancellationToken.None));
        }

        await Task.WhenAll(tasks);
    }

    [Benchmark(Description = "Single status publish")]
    public async Task SingleStatusPublish()
    {
        await _publisher.PublishStatusAsync(_runs[0], CancellationToken.None);
    }

    [Benchmark(Description = "Single log publish")]
    public async Task SingleLogPublish()
    {
        await _publisher.PublishLogAsync(_logEvents[0], CancellationToken.None);
    }
}

internal sealed class MockHubContext : IHubContext<RunEventsHub>
{
    private readonly MockHubClients _clients = new();

    public IHubClients Clients => _clients;

    public IGroupManager Groups => throw new NotImplementedException();
}

internal sealed class MockHubClients : IHubClients
{
    private readonly MockClientProxy _proxy = new();

    public IClientProxy All => _proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Client(string connectionId) => _proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
    public IClientProxy Group(string groupName) => _proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
    public IClientProxy User(string userId) => _proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
}

internal sealed class MockClientProxy : IClientProxy
{
    public Task SendCoreAsync(string method, object?[]? args, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
