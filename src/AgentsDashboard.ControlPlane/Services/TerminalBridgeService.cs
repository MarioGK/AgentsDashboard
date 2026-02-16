using System.Collections.Concurrent;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TerminalBridgeService : ITerminalBridgeService, ITerminalReceiver
{
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly IOrchestratorStore _store;
    private readonly IHubContext<TerminalHub> _hubContext;
    private readonly ILogger<TerminalBridgeService> _logger;
    private readonly TerminalOptions _options;

    private readonly ConcurrentDictionary<string, SessionBridge> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _connectionToSession = new();

    private ITerminalHub? _terminalHub;
    private readonly SemaphoreSlim _hubLock = new(1, 1);

    public TerminalBridgeService(
        IMagicOnionClientFactory clientFactory,
        IOrchestratorStore store,
        IHubContext<TerminalHub> hubContext,
        IOptions<TerminalOptions> options,
        ILogger<TerminalBridgeService> logger)
    {
        _clientFactory = clientFactory;
        _store = store;
        _hubContext = hubContext;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string> CreateSessionAsync(
        string connectionId,
        string workerId,
        string? runId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default)
    {
        var session = new TerminalSessionDocument
        {
            WorkerId = workerId,
            RunId = runId,
            State = TerminalSessionState.Pending,
            Cols = cols,
            Rows = rows
        };

        await _store.CreateTerminalSessionAsync(session, cancellationToken);
        _logger.LogInformation("Created terminal session {SessionId} for worker {WorkerId}", session.Id, workerId);

        var cts = new CancellationTokenSource();
        var bridge = new SessionBridge(session.Id, workerId, cts);
        bridge.ConnectionIds.Add(connectionId);

        _sessions[session.Id] = bridge;
        _connectionToSession[connectionId] = session.Id;

        var hub = await GetOrConnectHubAsync(cancellationToken);

        await hub.OpenSessionAsync(new OpenTerminalSessionRequest
        {
            SessionId = session.Id,
            WorkerId = workerId,
            RunId = runId,
            Cols = cols,
            Rows = rows
        });

        return session.Id;
    }

    public async Task AttachSessionAsync(
        string connectionId,
        string sessionId,
        long lastSequence,
        CancellationToken cancellationToken = default)
    {
        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        if (session.State == TerminalSessionState.Closed)
            throw new InvalidOperationException($"Session {sessionId} is closed");

        if (!_sessions.TryGetValue(sessionId, out var bridge))
        {
            var cts = new CancellationTokenSource();
            bridge = new SessionBridge(sessionId, session.WorkerId, cts);
            _sessions[sessionId] = bridge;

            var hub = await GetOrConnectHubAsync(cancellationToken);
            await hub.ReattachSessionAsync(new ReattachTerminalSessionRequest
            {
                SessionId = sessionId,
                LastSequence = lastSequence
            });
        }

        bridge.ConnectionIds.Add(connectionId);
        _connectionToSession[connectionId] = sessionId;

        // Cancel any pending grace timer since a client reconnected
        bridge.CancelGraceTimer();

        // Replay missed audit events
        var missedEvents = await _store.GetTerminalAuditEventsAsync(
            sessionId,
            afterSequence: lastSequence,
            limit: _options.ReplayBufferEvents,
            cancellationToken: cancellationToken);

        foreach (var evt in missedEvents)
        {
            await _hubContext.Clients.Client(connectionId).SendAsync(
                "TerminalOutput",
                new
                {
                    sessionId,
                    sequence = evt.Sequence,
                    payloadBase64 = evt.PayloadBase64,
                    direction = evt.Direction
                },
                cancellationToken);
        }

        _logger.LogInformation(
            "Connection {ConnectionId} attached to session {SessionId}, replayed {Count} events",
            connectionId, sessionId, missedEvents.Count);
    }

    public async Task SendInputAsync(
        string sessionId,
        string payloadBase64,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out _))
            throw new InvalidOperationException($"No active bridge for session {sessionId}");

        await _store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
        {
            SessionId = sessionId,
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = payloadBase64
        }, cancellationToken);

        var hub = await GetOrConnectHubAsync(cancellationToken);
        await hub.SendInputAsync(new TerminalInputMessage
        {
            SessionId = sessionId,
            PayloadBase64 = payloadBase64
        });
    }

    public async Task ResizeAsync(
        string sessionId,
        int cols,
        int rows,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out _))
            throw new InvalidOperationException($"No active bridge for session {sessionId}");

        var session = await _store.GetTerminalSessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            session.Cols = cols;
            session.Rows = rows;
            session.LastSeenAtUtc = DateTime.UtcNow;
            await _store.UpdateTerminalSessionAsync(session, cancellationToken);
        }

        var hub = await GetOrConnectHubAsync(cancellationToken);
        await hub.ResizeSessionAsync(new TerminalResizeMessage
        {
            SessionId = sessionId,
            Cols = cols,
            Rows = rows
        });
    }

    public async Task CloseAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var hub = await GetOrConnectHubAsync(cancellationToken);
        await hub.CloseSessionAsync(new CloseTerminalSessionRequest
        {
            SessionId = sessionId
        });

        await _store.CloseTerminalSessionAsync(sessionId, "Closed by user", cancellationToken);

        if (_sessions.TryRemove(sessionId, out var bridge))
        {
            bridge.Dispose();
            foreach (var connId in bridge.ConnectionIds)
            {
                _connectionToSession.TryRemove(connId, out _);
            }
        }

        _logger.LogInformation("Terminal session {SessionId} closed by user", sessionId);
    }

    public async Task OnClientDisconnectedAsync(string connectionId)
    {
        if (!_connectionToSession.TryRemove(connectionId, out var sessionId))
            return;

        if (!_sessions.TryGetValue(sessionId, out var bridge))
            return;

        bridge.ConnectionIds.Remove(connectionId);

        if (bridge.ConnectionIds.Count > 0)
        {
            _logger.LogDebug(
                "Connection {ConnectionId} disconnected from session {SessionId}, {Remaining} connections remain",
                connectionId, sessionId, bridge.ConnectionIds.Count);
            return;
        }

        // No more connected clients - set to Disconnected and start grace timer
        _logger.LogInformation(
            "All clients disconnected from session {SessionId}, starting {GraceMinutes}m grace timer",
            sessionId, _options.ResumeGraceMinutes);

        var session = await _store.GetTerminalSessionAsync(sessionId);
        if (session is not null && session.State == TerminalSessionState.Active)
        {
            session.State = TerminalSessionState.Disconnected;
            session.LastSeenAtUtc = DateTime.UtcNow;
            await _store.UpdateTerminalSessionAsync(session);
        }

        bridge.StartGraceTimer(
            TimeSpan.FromMinutes(_options.ResumeGraceMinutes),
            () => OnGraceTimerExpiredAsync(sessionId));
    }

    // ── ITerminalReceiver callbacks (called by worker via MagicOnion) ─────

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

    // ── Async handlers for receiver callbacks ─────────────────────────────

    private async Task HandleSessionOpenedAsync(TerminalSessionOpenedMessage message)
    {
        try
        {
            var session = await _store.GetTerminalSessionAsync(message.SessionId);
            if (session is not null)
            {
                session.State = message.Success ? TerminalSessionState.Active : TerminalSessionState.Closed;
                session.LastSeenAtUtc = DateTime.UtcNow;
                if (!message.Success)
                {
                    session.ClosedAtUtc = DateTime.UtcNow;
                    session.CloseReason = message.Error ?? "Failed to open";
                }
                await _store.UpdateTerminalSessionAsync(session);
            }

            if (_sessions.TryGetValue(message.SessionId, out var bridge))
            {
                var payload = new
                {
                    sessionId = message.SessionId,
                    success = message.Success,
                    error = message.Error
                };

                foreach (var connId in bridge.ConnectionIds.ToArray())
                {
                    await _hubContext.Clients.Client(connId).SendAsync("TerminalSessionOpened", payload);
                }
            }

            _logger.LogInformation(
                "Terminal session {SessionId} opened: success={Success}",
                message.SessionId, message.Success);
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
            });

            if (_sessions.TryGetValue(message.SessionId, out var bridge))
            {
                var payload = new
                {
                    sessionId = message.SessionId,
                    sequence = message.Sequence,
                    payloadBase64 = message.PayloadBase64,
                    direction = message.Direction
                };

                foreach (var connId in bridge.ConnectionIds.ToArray())
                {
                    await _hubContext.Clients.Client(connId).SendAsync("TerminalOutput", payload);
                }
            }
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
            await _store.CloseTerminalSessionAsync(message.SessionId, message.Reason ?? "Closed by worker");

            if (_sessions.TryRemove(message.SessionId, out var bridge))
            {
                var payload = new
                {
                    sessionId = message.SessionId,
                    reason = message.Reason
                };

                foreach (var connId in bridge.ConnectionIds.ToArray())
                {
                    _connectionToSession.TryRemove(connId, out _);
                    await _hubContext.Clients.Client(connId).SendAsync("TerminalSessionClosed", payload);
                }

                bridge.Dispose();
            }

            _logger.LogInformation("Terminal session {SessionId} closed: {Reason}", message.SessionId, message.Reason);
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
            if (_sessions.TryGetValue(message.SessionId, out var bridge))
            {
                var payload = new
                {
                    sessionId = message.SessionId,
                    error = message.Error
                };

                foreach (var connId in bridge.ConnectionIds.ToArray())
                {
                    await _hubContext.Clients.Client(connId).SendAsync("TerminalSessionError", payload);
                }
            }

            _logger.LogWarning("Terminal session {SessionId} error: {Error}", message.SessionId, message.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session error for {SessionId}", message.SessionId);
        }
    }

    // ── Grace timer ────────────────────────────────────────────────────────

    private async Task OnGraceTimerExpiredAsync(string sessionId)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var bridge))
                return;

            // If clients reconnected while grace timer was running, do nothing
            if (bridge.ConnectionIds.Count > 0)
                return;

            _logger.LogInformation(
                "Grace timer expired for session {SessionId}, closing", sessionId);

            var hub = await GetOrConnectHubAsync(CancellationToken.None);
            await hub.CloseSessionAsync(new CloseTerminalSessionRequest
            {
                SessionId = sessionId
            });

            await _store.CloseTerminalSessionAsync(sessionId, "Grace timer expired");

            if (_sessions.TryRemove(sessionId, out var removed))
                removed.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing session {SessionId} after grace timer", sessionId);
        }
    }

    // ── Hub connection management ──────────────────────────────────────────

    private async Task<ITerminalHub> GetOrConnectHubAsync(CancellationToken cancellationToken)
    {
        if (_terminalHub is not null)
            return _terminalHub;

        await _hubLock.WaitAsync(cancellationToken);
        try
        {
            if (_terminalHub is not null)
                return _terminalHub;

            _terminalHub = await _clientFactory.ConnectTerminalHubAsync(this, cancellationToken);
            _logger.LogInformation("Connected to worker terminal hub");
            return _terminalHub;
        }
        finally
        {
            _hubLock.Release();
        }
    }

    // ── SessionBridge inner type ───────────────────────────────────────────

    private sealed class SessionBridge : IDisposable
    {
        public string SessionId { get; }
        public string WorkerId { get; }
        public HashSet<string> ConnectionIds { get; } = [];
        private CancellationTokenSource _cts;
        private CancellationTokenSource? _graceTimerCts;

        public SessionBridge(string sessionId, string workerId, CancellationTokenSource cts)
        {
            SessionId = sessionId;
            WorkerId = workerId;
            _cts = cts;
        }

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
                        await onExpired();
                }
                catch (OperationCanceledException)
                {
                    // Grace timer cancelled (client reconnected)
                }
            }, CancellationToken.None);
        }

        public void CancelGraceTimer()
        {
            if (_graceTimerCts is not null)
            {
                _graceTimerCts.Cancel();
                _graceTimerCts.Dispose();
                _graceTimerCts = null;
            }
        }

        public void Dispose()
        {
            CancelGraceTimer();
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
