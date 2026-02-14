using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.ControlPlane.Services;

public enum HarnessStatus
{
    Available,
    Unavailable,
    Unknown
}

public record HarnessHealth(string Name, HarnessStatus Status, string? Version);

public class HarnessHealthService(ILogger<HarnessHealthService> logger) : IHostedService, IDisposable
{
    private readonly Dictionary<string, HarnessHealth> _harnessStatus = new();
    private Timer? _timer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private static readonly string[] HarnessCommands = ["codex", "opencode", "claude", "zai"];

    public virtual IReadOnlyDictionary<string, HarnessHealth> GetAllHealth() => _harnessStatus;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = RefreshAsync(cancellationToken);
        _timer = new Timer(_ => _ = RefreshAsync(CancellationToken.None), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _lock.Dispose();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var tasks = HarnessCommands.Select(cmd => CheckHarnessAsync(cmd, cancellationToken));
            var results = await Task.WhenAll(tasks);

            _harnessStatus.Clear();
            foreach (var health in results)
            {
                _harnessStatus[health.Name] = health;
            }

            logger.LogDebug("Refreshed harness health: {Count} harnesses checked", results.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh harness health");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<HarnessHealth> CheckHarnessAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Cli.Wrap("which")
                .WithArguments(command)
                .ExecuteBufferedAsync(cancellationToken);

            if (result.ExitCode != 0)
            {
                return new HarnessHealth(command, HarnessStatus.Unavailable, null);
            }

            var version = await GetVersionAsync(command, cancellationToken);
            return new HarnessHealth(command, HarnessStatus.Available, version);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Harness {Command} not available", command);
            return new HarnessHealth(command, HarnessStatus.Unavailable, null);
        }
    }

    private async Task<string?> GetVersionAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Cli.Wrap(command)
                .WithArguments("--version")
                .ExecuteBufferedAsync(cancellationToken);

            if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                var firstLine = result.StandardOutput.Split('\n').FirstOrDefault()?.Trim();
                return firstLine?[..Math.Min(firstLine.Length, 50)];
            }
        }
        catch
        {
        }

        return null;
    }
}
