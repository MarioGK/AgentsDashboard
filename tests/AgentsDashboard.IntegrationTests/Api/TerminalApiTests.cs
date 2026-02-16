using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.IntegrationTests.Api;

[ClassDataSource<ApiTestFixture>(Shared = SharedType.Keyed, Key = "Api")]
public class TerminalApiTests(ApiTestFixture fixture)
{
    private OrchestratorStore GetStore() => fixture.Services.GetRequiredService<OrchestratorStore>();

    private static TerminalSessionDocument MakeSession(string workerId = "worker-1", string? runId = null) =>
        new()
        {
            WorkerId = workerId,
            RunId = runId,
            Cols = 120,
            Rows = 30,
            State = TerminalSessionState.Active
        };

    // ── Session CRUD ────────────────────────────────────────────────────

    [Test]
    public async Task CreateTerminalSession_ReturnsSessionWithId()
    {
        var store = GetStore();
        var session = MakeSession();

        var created = await store.CreateTerminalSessionAsync(session, CancellationToken.None);

        created.Should().NotBeNull();
        created.Id.Should().NotBeNullOrEmpty();
        created.WorkerId.Should().Be("worker-1");
        created.Cols.Should().Be(120);
        created.Rows.Should().Be(30);
        created.State.Should().Be(TerminalSessionState.Active);
    }

    [Test]
    public async Task GetTerminalSession_ReturnsCreatedSession()
    {
        var store = GetStore();
        var session = MakeSession();

        var created = await store.CreateTerminalSessionAsync(session, CancellationToken.None);
        var fetched = await store.GetTerminalSessionAsync(created.Id, CancellationToken.None);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.WorkerId.Should().Be("worker-1");
        fetched.Cols.Should().Be(120);
        fetched.Rows.Should().Be(30);
    }

    [Test]
    public async Task GetTerminalSession_ReturnsNull_WhenNotFound()
    {
        var store = GetStore();

        var fetched = await store.GetTerminalSessionAsync("nonexistent-session-id", CancellationToken.None);

        fetched.Should().BeNull();
    }

    [Test]
    public async Task UpdateTerminalSession_UpdatesFields()
    {
        var store = GetStore();
        var session = MakeSession();
        var created = await store.CreateTerminalSessionAsync(session, CancellationToken.None);

        created.State = TerminalSessionState.Disconnected;
        created.Cols = 200;
        created.Rows = 50;
        created.LastSeenAtUtc = DateTime.UtcNow;

        await store.UpdateTerminalSessionAsync(created, CancellationToken.None);

        var fetched = await store.GetTerminalSessionAsync(created.Id, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.State.Should().Be(TerminalSessionState.Disconnected);
        fetched.Cols.Should().Be(200);
        fetched.Rows.Should().Be(50);
    }

    [Test]
    public async Task UpdateTerminalSession_DoesNotThrow_WhenNotFound()
    {
        var store = GetStore();
        var phantom = new TerminalSessionDocument
        {
            Id = "does-not-exist",
            WorkerId = "w",
            State = TerminalSessionState.Active
        };

        await store.UpdateTerminalSessionAsync(phantom, CancellationToken.None);
    }

    [Test]
    public async Task CloseTerminalSession_SetsStateAndReason()
    {
        var store = GetStore();
        var session = MakeSession();
        var created = await store.CreateTerminalSessionAsync(session, CancellationToken.None);

        await store.CloseTerminalSessionAsync(created.Id, "user requested", CancellationToken.None);

        var fetched = await store.GetTerminalSessionAsync(created.Id, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.State.Should().Be(TerminalSessionState.Closed);
        fetched.CloseReason.Should().Be("user requested");
        fetched.ClosedAtUtc.Should().NotBeNull();
    }

    [Test]
    public async Task CloseTerminalSession_DoesNotThrow_WhenNotFound()
    {
        var store = GetStore();

        await store.CloseTerminalSessionAsync("nonexistent-id", "cleanup", CancellationToken.None);
    }

    // ── Session Filtering ───────────────────────────────────────────────

    [Test]
    public async Task ListActiveTerminalSessionsByWorker_ReturnsOnlyMatchingWorker()
    {
        var store = GetStore();
        var s1 = await store.CreateTerminalSessionAsync(MakeSession("worker-filter-a"), CancellationToken.None);
        var s2 = await store.CreateTerminalSessionAsync(MakeSession("worker-filter-a"), CancellationToken.None);
        var s3 = await store.CreateTerminalSessionAsync(MakeSession("worker-filter-b"), CancellationToken.None);

        var results = await store.ListActiveTerminalSessionsByWorkerAsync("worker-filter-a", CancellationToken.None);

        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().OnlyContain(s => s.WorkerId == "worker-filter-a");
    }

    [Test]
    public async Task ListActiveTerminalSessionsByWorker_ExcludesClosedSessions()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession("worker-closed-filter"), CancellationToken.None);
        await store.CloseTerminalSessionAsync(session.Id, "done", CancellationToken.None);

        var results = await store.ListActiveTerminalSessionsByWorkerAsync("worker-closed-filter", CancellationToken.None);

        results.Should().NotContain(s => s.Id == session.Id);
    }

    [Test]
    public async Task ListTerminalSessionsByRun_ReturnsOnlyMatchingRun()
    {
        var store = GetStore();
        var runId = Guid.NewGuid().ToString("N");
        var otherRunId = Guid.NewGuid().ToString("N");
        await store.CreateTerminalSessionAsync(MakeSession("w1", runId), CancellationToken.None);
        await store.CreateTerminalSessionAsync(MakeSession("w2", runId), CancellationToken.None);
        await store.CreateTerminalSessionAsync(MakeSession("w3", otherRunId), CancellationToken.None);

        var results = await store.ListTerminalSessionsByRunAsync(runId, CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(s => s.RunId == runId);
    }

    [Test]
    public async Task ListTerminalSessionsByRun_ReturnsEmpty_WhenNoMatch()
    {
        var store = GetStore();

        var results = await store.ListTerminalSessionsByRunAsync("no-such-run", CancellationToken.None);

        results.Should().BeEmpty();
    }

    // ── Concurrent Sessions ─────────────────────────────────────────────

    [Test]
    public async Task ConcurrentSessions_TrackedIndependently()
    {
        var store = GetStore();
        var s1 = await store.CreateTerminalSessionAsync(MakeSession("worker-concurrent-1"), CancellationToken.None);
        var s2 = await store.CreateTerminalSessionAsync(MakeSession("worker-concurrent-2"), CancellationToken.None);

        s1.Id.Should().NotBe(s2.Id);

        await store.CloseTerminalSessionAsync(s1.Id, "closed first", CancellationToken.None);

        var fetched1 = await store.GetTerminalSessionAsync(s1.Id, CancellationToken.None);
        var fetched2 = await store.GetTerminalSessionAsync(s2.Id, CancellationToken.None);

        fetched1!.State.Should().Be(TerminalSessionState.Closed);
        fetched2!.State.Should().Be(TerminalSessionState.Active);
    }

    [Test]
    public async Task ConcurrentSessions_ParallelCreation_NoDuplicateIds()
    {
        var store = GetStore();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => store.CreateTerminalSessionAsync(MakeSession($"worker-parallel-{i}"), CancellationToken.None));

        var sessions = await Task.WhenAll(tasks);

        var ids = sessions.Select(s => s.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
        sessions.Should().HaveCount(10);
    }

    // ── Audit Events ────────────────────────────────────────────────────

    [Test]
    public async Task AppendTerminalAuditEvent_AssignsSequence()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        var auditEvent = new TerminalAuditEventDocument
        {
            SessionId = session.Id,
            Direction = TerminalDataDirection.Output,
            PayloadBase64 = Convert.ToBase64String("hello world"u8.ToArray())
        };

        await store.AppendTerminalAuditEventAsync(auditEvent, CancellationToken.None);

        auditEvent.Sequence.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task AppendTerminalAuditEvent_SequencesAreMonotonicallyIncreasing()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        var e1 = new TerminalAuditEventDocument
        {
            SessionId = session.Id,
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = Convert.ToBase64String("cmd1"u8.ToArray())
        };

        var e2 = new TerminalAuditEventDocument
        {
            SessionId = session.Id,
            Direction = TerminalDataDirection.Output,
            PayloadBase64 = Convert.ToBase64String("result1"u8.ToArray())
        };

        var e3 = new TerminalAuditEventDocument
        {
            SessionId = session.Id,
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = Convert.ToBase64String("cmd2"u8.ToArray())
        };

        await store.AppendTerminalAuditEventAsync(e1, CancellationToken.None);
        await store.AppendTerminalAuditEventAsync(e2, CancellationToken.None);
        await store.AppendTerminalAuditEventAsync(e3, CancellationToken.None);

        e1.Sequence.Should().BeLessThan(e2.Sequence);
        e2.Sequence.Should().BeLessThan(e3.Sequence);
    }

    [Test]
    public async Task GetTerminalAuditEvents_ReturnsAllEventsForSession()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        for (int i = 0; i < 5; i++)
        {
            await store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
            {
                SessionId = session.Id,
                Direction = i % 2 == 0 ? TerminalDataDirection.Input : TerminalDataDirection.Output,
                PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"data-{i}"))
            }, CancellationToken.None);
        }

        var events = await store.GetTerminalAuditEventsAsync(session.Id, cancellationToken: CancellationToken.None);

        events.Should().HaveCount(5);
        events.Should().BeInAscendingOrder(e => e.Sequence);
    }

    [Test]
    public async Task GetTerminalAuditEvents_ReturnsEmpty_WhenNoEvents()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        var events = await store.GetTerminalAuditEventsAsync(session.Id, cancellationToken: CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Test]
    public async Task GetTerminalAuditEvents_ReplayFromSequenceOffset()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        var sequences = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var evt = new TerminalAuditEventDocument
            {
                SessionId = session.Id,
                Direction = TerminalDataDirection.Output,
                PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"line-{i}"))
            };
            await store.AppendTerminalAuditEventAsync(evt, CancellationToken.None);
            sequences.Add(evt.Sequence);
        }

        var midpoint = sequences[4];
        var events = await store.GetTerminalAuditEventsAsync(session.Id, afterSequence: midpoint, cancellationToken: CancellationToken.None);

        events.Should().HaveCount(5);
        events.First().Sequence.Should().BeGreaterThan(midpoint);
        events.Should().BeInAscendingOrder(e => e.Sequence);
    }

    [Test]
    public async Task GetTerminalAuditEvents_RespectsLimit()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        for (int i = 0; i < 20; i++)
        {
            await store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
            {
                SessionId = session.Id,
                Direction = TerminalDataDirection.Output,
                PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"chunk-{i}"))
            }, CancellationToken.None);
        }

        var events = await store.GetTerminalAuditEventsAsync(session.Id, limit: 7, cancellationToken: CancellationToken.None);

        events.Should().HaveCount(7);
    }

    [Test]
    public async Task GetTerminalAuditEvents_PreservesDirectionAndPayload()
    {
        var store = GetStore();
        var session = await store.CreateTerminalSessionAsync(MakeSession(), CancellationToken.None);

        var payload = Convert.ToBase64String("ls -la\n"u8.ToArray());
        await store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
        {
            SessionId = session.Id,
            Direction = TerminalDataDirection.Input,
            PayloadBase64 = payload
        }, CancellationToken.None);

        var events = await store.GetTerminalAuditEventsAsync(session.Id, cancellationToken: CancellationToken.None);

        events.Should().HaveCount(1);
        events[0].Direction.Should().Be(TerminalDataDirection.Input);
        events[0].PayloadBase64.Should().Be(payload);
        events[0].SessionId.Should().Be(session.Id);
    }

    [Test]
    public async Task AuditEvents_AcrossSessions_AreIndependent()
    {
        var store = GetStore();
        var session1 = await store.CreateTerminalSessionAsync(MakeSession("w-audit-1"), CancellationToken.None);
        var session2 = await store.CreateTerminalSessionAsync(MakeSession("w-audit-2"), CancellationToken.None);

        for (int i = 0; i < 3; i++)
        {
            await store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
            {
                SessionId = session1.Id,
                Direction = TerminalDataDirection.Output,
                PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"s1-{i}"))
            }, CancellationToken.None);
        }

        for (int i = 0; i < 5; i++)
        {
            await store.AppendTerminalAuditEventAsync(new TerminalAuditEventDocument
            {
                SessionId = session2.Id,
                Direction = TerminalDataDirection.Input,
                PayloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"s2-{i}"))
            }, CancellationToken.None);
        }

        var events1 = await store.GetTerminalAuditEventsAsync(session1.Id, cancellationToken: CancellationToken.None);
        var events2 = await store.GetTerminalAuditEventsAsync(session2.Id, cancellationToken: CancellationToken.None);

        events1.Should().HaveCount(3);
        events2.Should().HaveCount(5);

        events1.Should().OnlyContain(e => e.SessionId == session1.Id);
        events2.Should().OnlyContain(e => e.SessionId == session2.Id);
    }
}
