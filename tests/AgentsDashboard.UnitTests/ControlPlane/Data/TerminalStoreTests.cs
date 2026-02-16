using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.UnitTests.ControlPlane.Data;

public class TerminalStoreTests
{
    // ── TerminalSessionDocument defaults ──────────────────────────────────

    [Test]
    public void TerminalSessionDocument_HasDefaults()
    {
        var session = new TerminalSessionDocument();

        session.Id.Should().NotBeNullOrEmpty();
        session.WorkerId.Should().BeEmpty();
        session.RunId.Should().BeNull();
        session.State.Should().Be(TerminalSessionState.Pending);
        session.Cols.Should().Be(80);
        session.Rows.Should().Be(24);
        session.ClosedAtUtc.Should().BeNull();
        session.CloseReason.Should().BeNull();
    }

    [Test]
    public void TerminalSessionDocument_Id_IsGenerated()
    {
        var s1 = new TerminalSessionDocument();
        var s2 = new TerminalSessionDocument();

        s1.Id.Should().NotBe(s2.Id);
        s1.Id.Should().HaveLength(32);
    }

    [Test]
    public void TerminalSessionDocument_CreatedAtUtc_IsRecentUtc()
    {
        var session = new TerminalSessionDocument();
        session.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void TerminalSessionDocument_LastSeenAtUtc_IsRecentUtc()
    {
        var session = new TerminalSessionDocument();
        session.LastSeenAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void TerminalSessionDocument_WithCustomProperties()
    {
        var session = new TerminalSessionDocument
        {
            Id = "custom-id",
            WorkerId = "worker-1",
            RunId = "run-123",
            State = TerminalSessionState.Active,
            Cols = 120,
            Rows = 40,
            CloseReason = "User closed"
        };

        session.Id.Should().Be("custom-id");
        session.WorkerId.Should().Be("worker-1");
        session.RunId.Should().Be("run-123");
        session.State.Should().Be(TerminalSessionState.Active);
        session.Cols.Should().Be(120);
        session.Rows.Should().Be(40);
        session.CloseReason.Should().Be("User closed");
    }

    // ── TerminalSessionState enum ─────────────────────────────────────────

    [Test]
    public void TerminalSessionState_HasExpectedValues()
    {
        ((int)TerminalSessionState.Pending).Should().Be(0);
        ((int)TerminalSessionState.Active).Should().Be(1);
        ((int)TerminalSessionState.Disconnected).Should().Be(2);
        ((int)TerminalSessionState.Closed).Should().Be(3);
    }

    [Test]
    [Arguments(TerminalSessionState.Pending)]
    [Arguments(TerminalSessionState.Active)]
    [Arguments(TerminalSessionState.Disconnected)]
    [Arguments(TerminalSessionState.Closed)]
    public void TerminalSessionState_CanBeAssigned(TerminalSessionState state)
    {
        var session = new TerminalSessionDocument { State = state };
        session.State.Should().Be(state);
    }

    // ── TerminalAuditEventDocument defaults ───────────────────────────────

    [Test]
    public void TerminalAuditEventDocument_HasDefaults()
    {
        var evt = new TerminalAuditEventDocument();

        evt.Id.Should().Be(0);
        evt.SessionId.Should().BeEmpty();
        evt.Sequence.Should().Be(0);
        evt.PayloadBase64.Should().BeEmpty();
    }

    [Test]
    public void TerminalAuditEventDocument_TimestampUtc_IsRecentUtc()
    {
        var evt = new TerminalAuditEventDocument();
        evt.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void TerminalAuditEventDocument_WithCustomProperties()
    {
        var evt = new TerminalAuditEventDocument
        {
            Id = 42,
            SessionId = "session-1",
            Sequence = 7,
            Direction = TerminalDataDirection.Output,
            PayloadBase64 = "aGVsbG8="
        };

        evt.Id.Should().Be(42);
        evt.SessionId.Should().Be("session-1");
        evt.Sequence.Should().Be(7);
        evt.Direction.Should().Be(TerminalDataDirection.Output);
        evt.PayloadBase64.Should().Be("aGVsbG8=");
    }

    // ── TerminalDataDirection enum ────────────────────────────────────────

    [Test]
    public void TerminalDataDirection_HasExpectedValues()
    {
        ((int)TerminalDataDirection.Input).Should().Be(0);
        ((int)TerminalDataDirection.Output).Should().Be(1);
    }

    [Test]
    [Arguments(TerminalDataDirection.Input)]
    [Arguments(TerminalDataDirection.Output)]
    public void TerminalDataDirection_CanBeAssigned(TerminalDataDirection direction)
    {
        var evt = new TerminalAuditEventDocument { Direction = direction };
        evt.Direction.Should().Be(direction);
    }

    // ── TerminalSessionDocument state transitions ─────────────────────────

    [Test]
    public void TerminalSessionDocument_CanTransitionFromPendingToActive()
    {
        var session = new TerminalSessionDocument { State = TerminalSessionState.Pending };
        session.State = TerminalSessionState.Active;
        session.State.Should().Be(TerminalSessionState.Active);
    }

    [Test]
    public void TerminalSessionDocument_CanTransitionFromActiveToDisconnected()
    {
        var session = new TerminalSessionDocument { State = TerminalSessionState.Active };
        session.State = TerminalSessionState.Disconnected;
        session.State.Should().Be(TerminalSessionState.Disconnected);
    }

    [Test]
    public void TerminalSessionDocument_CanTransitionFromActiveToClosed()
    {
        var session = new TerminalSessionDocument { State = TerminalSessionState.Active };
        session.State = TerminalSessionState.Closed;
        session.ClosedAtUtc = DateTime.UtcNow;
        session.CloseReason = "Closed by user";

        session.State.Should().Be(TerminalSessionState.Closed);
        session.ClosedAtUtc.Should().NotBeNull();
        session.CloseReason.Should().Be("Closed by user");
    }

    [Test]
    public void TerminalSessionDocument_CanTransitionFromDisconnectedToClosed()
    {
        var session = new TerminalSessionDocument { State = TerminalSessionState.Disconnected };
        session.State = TerminalSessionState.Closed;
        session.State.Should().Be(TerminalSessionState.Closed);
    }

    // ── TerminalAuditEventDocument direction tracking ─────────────────────

    [Test]
    public void TerminalAuditEvent_InputDirection_RecordsUserInput()
    {
        var evt = new TerminalAuditEventDocument
        {
            SessionId = "session-1",
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = Convert.ToBase64String("hello"u8.ToArray())
        };

        evt.Direction.Should().Be(TerminalDataDirection.Input);
        evt.PayloadBase64.Should().NotBeEmpty();
    }

    [Test]
    public void TerminalAuditEvent_OutputDirection_RecordsTerminalOutput()
    {
        var evt = new TerminalAuditEventDocument
        {
            SessionId = "session-1",
            Direction = TerminalDataDirection.Output,
            PayloadBase64 = Convert.ToBase64String("response"u8.ToArray())
        };

        evt.Direction.Should().Be(TerminalDataDirection.Output);
    }
}
