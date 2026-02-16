using System.Collections.Concurrent;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TerminalBridgeService : ITerminalBridgeService, ITerminalReceiver
{
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly IWorkerLifecycleManager _lifecycleManager;
    private readonly IOrchestratorStore _store;
    private readonly ILogger<TerminalBridgeService> _logger;
    private readonly TerminalOptions _options;

    private readonly ConcurrentDictionary<string, SessionBridge> _sessions = [];
    private readonly ConcurrentDictionary<string, TerminalClientCallbacks> _clients = [];
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _clientToSessions = [];
    private readonly ConcurrentDictionary<string, ITerminalHub> _hubs = [];
    private readonly SemaphoreSlim _hubsLock = new(1, 1);

    public TerminalBridgeService(
        IMagicOnionClientFactory clientFactory,
        IWorkerLifecycleManager lifecycleManager,
        IOrchestratorStore store,
        IOptions<TerminalOptions> options,
        ILogger<TerminalBridgeService> logger)
    {
        _clientFactory = clientFactory;
        _lifecycleManager = lifecycleManager;
        _store = store;
        _logger = logger;
        _options = options.Value;
    }

    public void RegisterClient(string clientId, TerminalClientCallbacks callbacks)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        _clients[clientId] = callbacks;
    }

    public async Task UnregisterClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        _clients.TryRemove(clientId, out _);

        if (!_clientToSessions.TryRemove(clientId, out var sessions))
        {
            return;
        }

        foreach (var sessionId in sessions.Keys)
        {
            await RemoveClientFromSessionAsync(clientId, sessionId, cancellationToken);
        }
    }

    public async Task<string> CreateSessionAsync(
        string clientId,
        string? workerId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default)
    {
        EnsureClientIsRegistered(clientId);
        var resolvedWorkerId = await ResolveWorkerForTerminalAsync(cancellationToken);

        var session = new TerminalSessionDocument
        {
            WorkerId = resolvedWorkerId,
            RunId = null,
            State = TerminalSessionState.Pending,
            Cols = cols,
            Rows = rows
        };

        await _store.CreateTerminalSessionAsync(session, cancellationToken);

        var bridge = new SessionBridge(session.Id, resolvedWorkerId);
        _sessions[session.Id] = bridge;
        AddClientToSession(clientId, session.Id, bridge);

        await ExecuteHubActionWithReconnectAsync(resolvedWorkerId, hub => hub.OpenSessionAsync(new OpenTerminalSessionRequest
        {
            SessionId = session.Id,
            WorkerId = resolvedWorkerId,
            RunId = null,
            Cols = cols,
            Rows = rows
        }), cancellationToken);

        _logger.LogInformation("Created terminal session {SessionId} for worker {WorkerId}", session.Id, resolvedWorkerId);
        return session.Id;
    }

    private async Task<string> ResolveWorkerForTerminalAsync(CancellationToken cancellationToken)
    {
        var lease = await _lifecycleManager.AcquireWorkerForDispatchAsync(cancellationToken);
        if (lease is not null)
        {
            return lease.WorkerId;
        }

        throw new InvalidOperationException("No online workers available for terminal session.");
    }

    public async Task AttachSessionAsync(
        string clientId,
        string sessionId,
        long lastSequence,
        CancellationToken cancellationToken = default)
    {
        EnsureClientIsRegistered(clientId);

        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        if (session.State == TerminalSessionState.Closed)
        {
            throw new InvalidOperationException($"Session {sessionId} is closed");
        }

        if (!_sessions.TryGetValue(sessionId, out var bridge))
        {
            bridge = new SessionBridge(sessionId, session.WorkerId);
            _sessions[sessionId] = bridge;

            await ExecuteHubActionWithReconnectAsync(session.WorkerId, hub => hub.ReattachSessionAsync(new ReattachTerminalSessionRequest
            {
                SessionId = sessionId,
                LastSequence = lastSequence
            }), cancellationToken);
        }

        AddClientToSession(clientId, sessionId, bridge);
        bridge.CancelGraceTimer();

        var missedEvents = await _store.GetTerminalAuditEventsAsync(
            sessionId,
            afterSequence: lastSequence,
            limit: _options.ReplayBufferEvents,
            cancellationToken: cancellationToken);

        if (_clients.TryGetValue(clientId, out var callbacks))
        {
            foreach (var evt in missedEvents)
            {
                await callbacks.OnSessionOutputAsync(new TerminalOutputMessage
                {
                    SessionId = sessionId,
                    Sequence = evt.Sequence,
                    PayloadBase64 = evt.PayloadBase64,
                    Direction = evt.Direction
                });
            }
        }

        _logger.LogInformation(
            "Client {ClientId} attached to session {SessionId}, replayed {Count} events",
            clientId,
            sessionId,
            missedEvents.Count);
    }

    public async Task SendInputAsync(
        string sessionId,
        string payloadBase64,
        CancellationToken cancellationToken = default)
    {
        var bridge = GetBridgeOrThrow(sessionId);

        await _store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
        {
            SessionId = sessionId,
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = payloadBase64
        }, cancellationToken);

        await ExecuteHubActionWithReconnectAsync(bridge.WorkerId, hub => hub.SendInputAsync(new TerminalInputMessage
        {
            SessionId = sessionId,
            PayloadBase64 = payloadBase64
        }), cancellationToken);
    }

    public async Task ResizeAsync(
        string sessionId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default)
    {
        var bridge = GetBridgeOrThrow(sessionId);

        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            session.Cols = cols;
            session.Rows = rows;
            session.LastSeenAtUtc = DateTime.UtcNow;
            await _store.UpdateTerminalSessionAsync(session, cancellationToken);
        }

        await ExecuteHubActionWithReconnectAsync(bridge.WorkerId, hub => hub.ResizeSessionAsync(new TerminalResizeMessage
        {
            SessionId = sessionId,
            Cols = cols,
            Rows = rows
        }), cancellationToken);
    }

    public async Task CloseAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken);
        var workerId = session?.WorkerId;

        if (_sessions.TryRemove(sessionId, out var bridge))
        {
            workerId = bridge.WorkerId;
            foreach (var clientId in bridge.ClientIds.Keys)
            {
                if (_clientToSessions.TryGetValue(clientId, out var sessions))
                {
                    sessions.TryRemove(sessionId, out _);
                    if (sessions.IsEmpty)
                    {
                        _clientToSessions.TryRemove(clientId, out _);
                    }
                }
            }

            bridge.Dispose();
        }

        if (!string.IsNullOrWhiteSpace(workerId))
        {
            await ExecuteHubActionWithReconnectAsync(workerId, hub => hub.CloseSessionAsync(new CloseTerminalSessionRequest
            {
                SessionId = sessionId
            }), cancellationToken);
        }

        await _store.CloseTerminalSessionAsync(sessionId, "Closed by user", cancellationToken);

        if (!string.IsNullOrWhiteSpace(workerId))
        {
            await TryDisposeWorkerHubIfUnusedAsync(workerId, cancellationToken);

            if (session?.RunId is null)
            {
                try
                {
                    await _lifecycleManager.RecycleWorkerAsync(workerId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed recycling standalone terminal worker {WorkerId}", workerId);
                }
            }
        }

        _logger.LogInformation("Terminal session {SessionId} closed by user", sessionId);
    }

    void ITerminalReceiver.OnSessionOpened(TerminalSessionOpenedMessage message)
    {
        _ = HandleSessionOpenedAsync(message);
    }

    void ITerminalReceiver.OnSessionOutput(TerminalOutputMessage message)
    {
        _ = HandleSessionOutputAsync(message);
    }

    void ITerminalReceiver.OnSessionClosed(TerminalSessionClosedMessage message)
    {
        _ = HandleSessionClosedAsync(message);
    }

    void ITerminalReceiver.OnSessionError(TerminalSessionErrorMessage message)
    {
        _ = HandleSessionErrorAsync(message);
    }

    private async Task HandleSessionOpenedAsync(TerminalSessionOpenedMessage message)
    {
        try
        {
            var session = await _store.GetTerminalSessionAsync(message.SessionId, CancellationToken.None);
            if (session is not null)
            {
                session.State = message.Success ? TerminalSessionState.Active : TerminalSessionState.Closed;
                session.LastSeenAtUtc = DateTime.UtcNow;
                if (!message.Success)
                {
                    session.ClosedAtUtc = DateTime.UtcNow;
                    session.CloseReason = message.Error ?? "Failed to open";
                }

                await _store.UpdateTerminalSessionAsync(session, CancellationToken.None);
            }

            await PublishToSessionClientsAsync(
                message.SessionId,
                callbacks => callbacks.OnSessionOpenedAsync(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session opened for {SessionId}", message.SessionId);
        }
    }

    private async Task HandleSessionOutputAsync(TerminalOutputMessage message)
    {
        try
        {
            await _store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
            {
                SessionId = message.SessionId,
                Direction = TerminalDataDirection.Output,
                PayloadBase64 = message.PayloadBase64
            }, CancellationToken.None);

            await PublishToSessionClientsAsync(
                message.SessionId,
                callbacks => callbacks.OnSessionOutputAsync(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session output for {SessionId}", message.SessionId);
        }
    }

    private async Task HandleSessionClosedAsync(TerminalSessionClosedMessage message)
    {
        try
        {
            var session = await _store.GetTerminalSessionAsync(message.SessionId, CancellationToken.None);
            await _store.CloseTerminalSessionAsync(message.SessionId, message.Reason ?? "Closed by worker", CancellationToken.None);

            await PublishToSessionClientsAsync(
                message.SessionId,
                callbacks => callbacks.OnSessionClosedAsync(message));

            if (_sessions.TryRemove(message.SessionId, out var bridge))
            {
                foreach (var clientId in bridge.ClientIds.Keys)
                {
                    if (_clientToSessions.TryGetValue(clientId, out var sessions))
                    {
                        sessions.TryRemove(message.SessionId, out _);
                        if (sessions.IsEmpty)
                        {
                            _clientToSessions.TryRemove(clientId, out _);
                        }
                    }
                }

                bridge.Dispose();
                await TryDisposeWorkerHubIfUnusedAsync(bridge.WorkerId, CancellationToken.None);

                if (session?.RunId is null)
                {
                    try
                    {
                        await _lifecycleManager.RecycleWorkerAsync(bridge.WorkerId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed recycling standalone terminal worker {WorkerId}", bridge.WorkerId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session closed for {SessionId}", message.SessionId);
        }
    }

    private async Task HandleSessionErrorAsync(TerminalSessionErrorMessage message)
    {
        try
        {
            await PublishToSessionClientsAsync(
                message.SessionId,
                callbacks => callbacks.OnSessionErrorAsync(message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session error for {SessionId}", message.SessionId);
        }
    }

    private async Task OnGraceTimerExpiredAsync(string sessionId)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var bridge))
            {
                return;
            }

            if (!bridge.ClientIds.IsEmpty)
            {
                return;
            }

            var session = await _store.GetTerminalSessionAsync(sessionId, CancellationToken.None);

            await ExecuteHubActionWithReconnectAsync(bridge.WorkerId, hub => hub.CloseSessionAsync(new CloseTerminalSessionRequest
            {
                SessionId = sessionId
            }), CancellationToken.None);

            await _store.CloseTerminalSessionAsync(sessionId, "Grace timer expired", CancellationToken.None);

            if (_sessions.TryRemove(sessionId, out var removed))
            {
                removed.Dispose();
            }

            await TryDisposeWorkerHubIfUnusedAsync(bridge.WorkerId, CancellationToken.None);

            if (session?.RunId is null)
            {
                try
                {
                    await _lifecycleManager.RecycleWorkerAsync(bridge.WorkerId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed recycling standalone terminal worker {WorkerId}", bridge.WorkerId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session {SessionId} after grace timer", sessionId);
        }
    }

    private SessionBridge GetBridgeOrThrow(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var bridge))
        {
            return bridge;
        }

        throw new InvalidOperationException($"No active bridge for session {sessionId}");
    }

    private void EnsureClientIsRegistered(string clientId)
    {
        if (!_clients.ContainsKey(clientId))
        {
            throw new InvalidOperationException($"Client {clientId} is not registered");
        }
    }

    private void AddClientToSession(string clientId, string sessionId, SessionBridge bridge)
    {
        bridge.ClientIds[clientId] = 0;

        var sessions = _clientToSessions.GetOrAdd(clientId, static _ => []);
        sessions[sessionId] = 0;
    }

    private async Task RemoveClientFromSessionAsync(string clientId, string sessionId, CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var bridge))
        {
            return;
        }

        bridge.ClientIds.TryRemove(clientId, out _);

        if (!bridge.ClientIds.IsEmpty)
        {
            return;
        }

        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken);
        if (session is not null && session.State == TerminalSessionState.Active)
        {
            session.State = TerminalSessionState.Disconnected;
            session.LastSeenAtUtc = DateTime.UtcNow;
            await _store.UpdateTerminalSessionAsync(session, cancellationToken);
        }

        bridge.StartGraceTimer(
            TimeSpan.FromMinutes(_options.ResumeGraceMinutes),
            () => OnGraceTimerExpiredAsync(sessionId));
    }

    private async Task PublishToSessionClientsAsync(string sessionId, Func<TerminalClientCallbacks, Task> action)
    {
        if (!_sessions.TryGetValue(sessionId, out var bridge))
        {
            return;
        }

        foreach (var clientId in bridge.ClientIds.Keys.ToArray())
        {
            if (!_clients.TryGetValue(clientId, out var callbacks))
            {
                continue;
            }

            try
            {
                await action(callbacks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed delivering terminal event to client {ClientId}", clientId);
            }
        }
    }

    private async Task<ITerminalHub> GetOrConnectHubAsync(string workerId, CancellationToken cancellationToken)
    {
        if (_hubs.TryGetValue(workerId, out var existing))
        {
            return existing;
        }

        await _hubsLock.WaitAsync(cancellationToken);
        try
        {
            if (_hubs.TryGetValue(workerId, out existing))
            {
                return existing;
            }

            var worker = await _lifecycleManager.GetWorkerAsync(workerId, cancellationToken);
            if (worker is null || !worker.IsRunning)
            {
                throw new InvalidOperationException($"Worker {workerId} is unavailable for terminal connection.");
            }

            var hub = await _clientFactory.ConnectTerminalHubAsync(worker.WorkerId, worker.GrpcEndpoint, this, cancellationToken);
            _hubs[workerId] = hub;
            _logger.LogInformation("Connected to terminal hub for worker {WorkerId}", workerId);
            return hub;
        }
        finally
        {
            _hubsLock.Release();
        }
    }

    private async Task ExecuteHubActionWithReconnectAsync(
        string workerId,
        Func<ITerminalHub, Task> action,
        CancellationToken cancellationToken)
    {
        var hub = await GetOrConnectHubAsync(workerId, cancellationToken);

        try
        {
            await action(hub);
        }
        catch (Exception ex) when (IsDisconnectedHubException(ex))
        {
            _logger.LogWarning(ex, "Terminal hub disconnected for worker {WorkerId}. Reconnecting and retrying once.", workerId);
            await InvalidateHubAsync(workerId, cancellationToken);
            hub = await GetOrConnectHubAsync(workerId, cancellationToken);
            await action(hub);
        }
    }

    private static bool IsDisconnectedHubException(Exception ex)
    {
        if (ex is RpcException rpc && rpc.StatusCode == StatusCode.Unavailable)
        {
            return true;
        }

        if (ex.Message.Contains("already been disconnected from the server", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsDisconnectedHubException(ex.InnerException);
    }

    private async Task InvalidateHubAsync(string workerId, CancellationToken cancellationToken)
    {
        ITerminalHub? hubToDispose = null;

        await _hubsLock.WaitAsync(cancellationToken);
        try
        {
            if (_hubs.TryRemove(workerId, out var existing))
            {
                hubToDispose = existing;
            }
        }
        finally
        {
            _hubsLock.Release();
        }

        if (hubToDispose is null)
        {
            return;
        }

        try
        {
            await hubToDispose.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to dispose terminal hub for worker {WorkerId}", workerId);
        }
    }

    private async Task TryDisposeWorkerHubIfUnusedAsync(string workerId, CancellationToken cancellationToken)
    {
        if (_sessions.Values.Any(x => string.Equals(x.WorkerId, workerId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        await InvalidateHubAsync(workerId, cancellationToken);
    }

    private sealed class SessionBridge : IDisposable
    {
        public SessionBridge(string sessionId, string workerId)
        {
            SessionId = sessionId;
            WorkerId = workerId;
        }

        public string SessionId { get; }
        public string WorkerId { get; }
        public ConcurrentDictionary<string, byte> ClientIds { get; } = [];
        private CancellationTokenSource? _graceTimerCts;

        public void StartGraceTimer(TimeSpan delay, Func<Task> onExpired)
        {
            CancelGraceTimer();
            _graceTimerCts = new CancellationTokenSource();
            var token = _graceTimerCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delay, token);
                    if (!token.IsCancellationRequested)
                    {
                        await onExpired();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);
        }

        public void CancelGraceTimer()
        {
            if (_graceTimerCts is null)
            {
                return;
            }

            _graceTimerCts.Cancel();
            _graceTimerCts.Dispose();
            _graceTimerCts = null;
        }

        public void Dispose()
        {
            CancelGraceTimer();
        }
    }
}
