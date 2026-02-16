using System.Collections.Concurrent;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Configuration;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class TerminalSessionManager : ITerminalSessionManager
{
    private readonly DockerClient _docker;
    private readonly ILogger<TerminalSessionManager> _logger;
    private readonly TerminalOptions _terminalOptions;
    private readonly WorkerOptions _workerOptions;
    private readonly ConcurrentDictionary<string, TerminalSessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, Func<byte[], TerminalDataDirection, CancellationToken, Task>> _outputCallbacks = new();
    private readonly Timer _idleTimer;

    public TerminalSessionManager(
        IOptions<TerminalOptions> terminalOptions,
        IOptions<WorkerOptions> workerOptions,
        ILogger<TerminalSessionManager> logger)
    {
        _logger = logger;
        _terminalOptions = terminalOptions.Value;
        _workerOptions = workerOptions.Value;
        _docker = new DockerClientConfiguration().CreateClient();
        _idleTimer = new Timer(CheckIdleSessions, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<TerminalSessionInfo> OpenSessionAsync(
        string sessionId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken)
    {
        if (_sessions.Count >= _terminalOptions.MaxConcurrentSessionsPerWorker)
            throw new InvalidOperationException(
                $"Maximum concurrent sessions ({_terminalOptions.MaxConcurrentSessionsPerWorker}) reached");

        string containerId;
        bool isStandalone;

        if (!string.IsNullOrEmpty(runId))
        {
            containerId = await FindContainerByRunIdAsync(runId, cancellationToken);
            isStandalone = false;
        }
        else
        {
            containerId = await CreateStandaloneContainerAsync(sessionId, cancellationToken);
            isStandalone = true;
        }

        var shell = await DetectShellAsync(containerId, cancellationToken);

        var execResponse = await _docker.Exec.ExecCreateContainerAsync(
            containerId,
            new ContainerExecCreateParameters
            {
                Cmd = [shell, "-l"],
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                Tty = true,
                Env = [$"TERM=xterm-256color", $"COLUMNS={cols}", $"LINES={rows}"],
            },
            cancellationToken);

        var session = new TerminalSessionInfo
        {
            SessionId = sessionId,
            RunId = runId,
            ContainerId = containerId,
            ExecId = execResponse.ID,
            Cols = cols,
            Rows = rows,
            IsStandaloneContainer = isStandalone,
        };

        if (!_sessions.TryAdd(sessionId, session))
        {
            if (isStandalone)
                await RemoveContainerSafeAsync(containerId, cancellationToken);
            throw new InvalidOperationException($"Session {sessionId} already exists");
        }

        _ = Task.Run(() => RunOutputLoopAsync(session), CancellationToken.None);

        _logger.LogInformation(
            "Opened terminal session {SessionId} on container {ContainerId} (run={RunId}, standalone={IsStandalone})",
            sessionId, containerId[..Math.Min(12, containerId.Length)], runId ?? "none", isStandalone);

        return session;
    }

    public async Task SendInputAsync(string sessionId, byte[] data, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException($"Session {sessionId} not found");

        session.LastActivityUtc = DateTime.UtcNow;

        var stream = await GetExecStreamAsync(session, cancellationToken);
        await stream.WriteAsync(data, 0, data.Length, cancellationToken);
    }

    public async Task ResizeAsync(string sessionId, int cols, int rows, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new InvalidOperationException($"Session {sessionId} not found");

        session.Cols = cols;
        session.Rows = rows;
        session.LastActivityUtc = DateTime.UtcNow;

        await _docker.Exec.ResizeContainerExecTtyAsync(
            session.ExecId,
            new ContainerResizeParameters { Height = rows, Width = cols },
            cancellationToken);
    }

    public async Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryRemove(sessionId, out var session))
            return;

        _outputCallbacks.TryRemove(sessionId, out _);

        await session.Cts.CancelAsync();

        if (session.IsStandaloneContainer)
            await RemoveContainerSafeAsync(session.ContainerId, cancellationToken);

        session.Cts.Dispose();

        _logger.LogInformation("Closed terminal session {SessionId}", sessionId);
    }

    public bool TryGetSession(string sessionId, out TerminalSessionInfo? session)
    {
        var found = _sessions.TryGetValue(sessionId, out var s);
        session = s;
        return found;
    }

    public void RegisterOutputCallback(
        string sessionId,
        Func<byte[], TerminalDataDirection, CancellationToken, Task> callback)
    {
        _outputCallbacks[sessionId] = callback;
    }

    public void UnregisterOutputCallback(string sessionId)
    {
        _outputCallbacks.TryRemove(sessionId, out _);
    }

    private async Task RunOutputLoopAsync(TerminalSessionInfo session)
    {
        var buffer = new byte[_terminalOptions.MaxChunkBytes];

        try
        {
            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                session.ExecId,
                tty: true,
                cancellationToken: session.Cts.Token);

            // Store the stream reference for input writes
            _execStreams[session.SessionId] = stream;

            while (!session.Cts.Token.IsCancellationRequested)
            {
                int bytesRead;
                try
                {
                    var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, session.Cts.Token);
                    bytesRead = result.Count;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    _logger.LogDebug(ex, "Output stream ended for session {SessionId}", session.SessionId);
                    break;
                }

                if (bytesRead == 0)
                    break;

                session.LastActivityUtc = DateTime.UtcNow;

                if (_outputCallbacks.TryGetValue(session.SessionId, out var callback))
                {
                    var chunk = new byte[bytesRead];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);

                    try
                    {
                        await callback(chunk, TerminalDataDirection.Output, session.Cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error in output callback for session {SessionId}", session.SessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Output loop failed for session {SessionId}", session.SessionId);
        }
        finally
        {
            _execStreams.TryRemove(session.SessionId, out _);

            if (_sessions.ContainsKey(session.SessionId))
            {
                _logger.LogInformation("Output loop ended for session {SessionId}, cleaning up", session.SessionId);
                await CloseSessionAsync(session.SessionId, CancellationToken.None);
            }
        }
    }

    private readonly ConcurrentDictionary<string, MultiplexedStream> _execStreams = new();

    private async Task<MultiplexedStream> GetExecStreamAsync(TerminalSessionInfo session, CancellationToken cancellationToken)
    {
        // Wait briefly for the output loop to populate the stream
        for (var i = 0; i < 50; i++)
        {
            if (_execStreams.TryGetValue(session.SessionId, out var stream))
                return stream;
            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException($"Exec stream not available for session {session.SessionId}");
    }

    private async Task<string> FindContainerByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["label"] = new Dictionary<string, bool>
                    {
                        [$"orchestrator.run-id={runId}"] = true
                    }
                }
            },
            cancellationToken);

        var container = containers.FirstOrDefault(c =>
            c.State is "running" or "created")
            ?? throw new InvalidOperationException($"No running container found for run {runId}");

        return container.ID;
    }

    private async Task<string> CreateStandaloneContainerAsync(string sessionId, CancellationToken cancellationToken)
    {
        var image = _terminalOptions.DefaultImage;

        var response = await _docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Image = image,
                Cmd = ["sleep", "infinity"],
                Tty = true,
                OpenStdin = true,
                Labels = new Dictionary<string, string>
                {
                    [$"{_workerOptions.ContainerLabelPrefix}.terminal-session-id"] = sessionId
                },
                HostConfig = new HostConfig
                {
                    AutoRemove = false,
                    NanoCPUs = 1_000_000_000,
                    Memory = 512 * 1024 * 1024,
                },
            },
            cancellationToken);

        await _docker.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), cancellationToken);

        _logger.LogInformation("Created standalone terminal container {ContainerId} for session {SessionId}",
            response.ID[..Math.Min(12, response.ID.Length)], sessionId);

        return response.ID;
    }

    private async Task<string> DetectShellAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                containerId,
                new ContainerExecCreateParameters
                {
                    Cmd = ["test", "-x", "/bin/bash"],
                    AttachStdout = true,
                    AttachStderr = true,
                },
                cancellationToken);

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID, tty: false, cancellationToken: cancellationToken);

            var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);
            return inspect.ExitCode == 0 ? "/bin/bash" : "/bin/sh";
        }
        catch
        {
            return "/bin/sh";
        }
    }

    private async Task RemoveContainerSafeAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                cancellationToken);
        }
        catch (DockerContainerNotFoundException)
        {
            // Already removed
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove standalone container {ContainerId}",
                containerId[..Math.Min(12, containerId.Length)]);
        }
    }

    private void CheckIdleSessions(object? state)
    {
        var idleTimeout = TimeSpan.FromMinutes(_terminalOptions.IdleTimeoutMinutes);
        var now = DateTime.UtcNow;

        foreach (var (sessionId, session) in _sessions)
        {
            if (now - session.LastActivityUtc > idleTimeout)
            {
                _logger.LogInformation("Session {SessionId} idle for {Minutes} minutes, closing",
                    sessionId, _terminalOptions.IdleTimeoutMinutes);
                _ = CloseSessionAsync(sessionId, CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();

        foreach (var (sessionId, _) in _sessions)
        {
            _ = CloseSessionAsync(sessionId, CancellationToken.None);
        }

        _docker.Dispose();
    }
}
