using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Services;
using MagicOnion.Server.Hubs;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.WorkerGateway.MagicOnion;

public sealed class TerminalHub : StreamingHubBase<ITerminalHub, ITerminalReceiver>, ITerminalHub
{
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ILogger<TerminalHub> _logger;
    private readonly HashSet<string> _ownedSessions = [];

    public TerminalHub(
        ITerminalSessionManager sessionManager,
        ILogger<TerminalHub> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    protected override ValueTask OnConnecting()
    {
        _logger.LogDebug("Terminal client connecting");
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnDisconnected()
    {
        _logger.LogDebug("Terminal client disconnected, cleaning up {Count} sessions", _ownedSessions.Count);

        foreach (var sessionId in _ownedSessions.ToList())
        {
            _sessionManager.UnregisterOutputCallback(sessionId);
        }

        _ownedSessions.Clear();
        await Task.CompletedTask;
    }

    public async Task OpenSessionAsync(OpenTerminalSessionRequest request)
    {
        try
        {
            var session = await _sessionManager.OpenSessionAsync(
                request.SessionId,
                request.RunId,
                request.Cols,
                request.Rows,
                Context.CallContext.CancellationToken);

            _ownedSessions.Add(request.SessionId);

            var receiver = Client;
            _sessionManager.RegisterOutputCallback(request.SessionId,
                async (data, direction, ct) =>
                {
                    var seq = Interlocked.Increment(ref session.CurrentSequence);
                    receiver.OnSessionOutput(new TerminalOutputMessage
                    {
                        SessionId = request.SessionId,
                        Sequence = seq,
                        PayloadBase64 = Convert.ToBase64String(data),
                        Direction = direction,
                    });
                    await Task.CompletedTask;
                });

            receiver.OnSessionOpened(new TerminalSessionOpenedMessage
            {
                SessionId = request.SessionId,
                Success = true,
            });

            _logger.LogInformation("Terminal session {SessionId} opened for run {RunId}",
                request.SessionId, request.RunId ?? "standalone");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open terminal session {SessionId}", request.SessionId);

            Client.OnSessionOpened(new TerminalSessionOpenedMessage
            {
                SessionId = request.SessionId,
                Success = false,
                Error = ex.Message,
            });
        }
    }

    public async Task SendInputAsync(TerminalInputMessage message)
    {
        try
        {
            var data = Convert.FromBase64String(message.PayloadBase64);
            await _sessionManager.SendInputAsync(
                message.SessionId,
                data,
                Context.CallContext.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send input to session {SessionId}", message.SessionId);

            Client.OnSessionError(new TerminalSessionErrorMessage
            {
                SessionId = message.SessionId,
                Error = ex.Message,
            });
        }
    }

    public async Task ResizeSessionAsync(TerminalResizeMessage message)
    {
        try
        {
            await _sessionManager.ResizeAsync(
                message.SessionId,
                message.Cols,
                message.Rows,
                Context.CallContext.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resize session {SessionId}", message.SessionId);
        }
    }

    public async Task CloseSessionAsync(CloseTerminalSessionRequest request)
    {
        try
        {
            _sessionManager.UnregisterOutputCallback(request.SessionId);
            _ownedSessions.Remove(request.SessionId);

            await _sessionManager.CloseSessionAsync(
                request.SessionId,
                Context.CallContext.CancellationToken);

            Client.OnSessionClosed(new TerminalSessionClosedMessage
            {
                SessionId = request.SessionId,
                Reason = "User closed session",
            });

            _logger.LogInformation("Terminal session {SessionId} closed by client", request.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to close terminal session {SessionId}", request.SessionId);

            Client.OnSessionError(new TerminalSessionErrorMessage
            {
                SessionId = request.SessionId,
                Error = ex.Message,
            });
        }
    }

    public async Task ReattachSessionAsync(ReattachTerminalSessionRequest request)
    {
        try
        {
            if (!_sessionManager.TryGetSession(request.SessionId, out var session) || session is null)
            {
                Client.OnSessionError(new TerminalSessionErrorMessage
                {
                    SessionId = request.SessionId,
                    Error = "Session not found or expired",
                });
                return;
            }

            _ownedSessions.Add(request.SessionId);

            var receiver = Client;
            _sessionManager.RegisterOutputCallback(request.SessionId,
                async (data, direction, ct) =>
                {
                    var seq = Interlocked.Increment(ref session.CurrentSequence);
                    receiver.OnSessionOutput(new TerminalOutputMessage
                    {
                        SessionId = request.SessionId,
                        Sequence = seq,
                        PayloadBase64 = Convert.ToBase64String(data),
                        Direction = direction,
                    });
                    await Task.CompletedTask;
                });

            receiver.OnSessionOpened(new TerminalSessionOpenedMessage
            {
                SessionId = request.SessionId,
                Success = true,
            });

            _logger.LogInformation("Reattached to terminal session {SessionId} from sequence {Seq}",
                request.SessionId, request.LastSequence);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reattach to terminal session {SessionId}", request.SessionId);

            Client.OnSessionError(new TerminalSessionErrorMessage
            {
                SessionId = request.SessionId,
                Error = ex.Message,
            });
        }
    }
}
