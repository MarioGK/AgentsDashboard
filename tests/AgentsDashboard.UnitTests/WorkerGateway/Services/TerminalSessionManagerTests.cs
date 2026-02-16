using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class TerminalSessionManagerTests
{
    // ── TerminalSessionInfo model tests ──────────────────────────────────

    [Test]
    public void TerminalSessionInfo_RequiredPropertiesCanBeSet()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "session-1",
            ContainerId = "container-abc",
            ExecId = "exec-xyz"
        };

        session.SessionId.Should().Be("session-1");
        session.ContainerId.Should().Be("container-abc");
        session.ExecId.Should().Be("exec-xyz");
    }

    [Test]
    public void TerminalSessionInfo_OptionalRunId_DefaultsNull()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.RunId.Should().BeNull();
    }

    [Test]
    public void TerminalSessionInfo_Cols_CanBeUpdated()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1",
            Cols = 80
        };

        session.Cols = 120;
        session.Cols.Should().Be(120);
    }

    [Test]
    public void TerminalSessionInfo_Rows_CanBeUpdated()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1",
            Rows = 24
        };

        session.Rows = 40;
        session.Rows.Should().Be(40);
    }

    [Test]
    public void TerminalSessionInfo_CurrentSequence_StartsAtZero()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.CurrentSequence.Should().Be(0);
    }

    [Test]
    public void TerminalSessionInfo_CurrentSequence_CanBeIncremented()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        var next = Interlocked.Increment(ref session.CurrentSequence);
        next.Should().Be(1);
        session.CurrentSequence.Should().Be(1);
    }

    [Test]
    public void TerminalSessionInfo_LastActivityUtc_DefaultsToNow()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.LastActivityUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void TerminalSessionInfo_CreatedAtUtc_DefaultsToNow()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void TerminalSessionInfo_IsStandaloneContainer_DefaultsFalse()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.IsStandaloneContainer.Should().BeFalse();
    }

    [Test]
    public void TerminalSessionInfo_Cts_IsNotCancelled()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.Cts.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public void TerminalSessionInfo_Cts_CanBeCancelled()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        session.Cts.Cancel();
        session.Cts.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void TerminalSessionInfo_WithRunId_SetsCorrectly()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1",
            RunId = "run-42"
        };

        session.RunId.Should().Be("run-42");
    }

    [Test]
    public void TerminalSessionInfo_WithStandalone_SetsCorrectly()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1",
            IsStandaloneContainer = true
        };

        session.IsStandaloneContainer.Should().BeTrue();
    }

    [Test]
    public void TerminalSessionInfo_ConcurrentSequenceIncrement_IsAtomic()
    {
        var session = new TerminalSessionInfo
        {
            SessionId = "s1",
            ContainerId = "c1",
            ExecId = "e1"
        };

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => Interlocked.Increment(ref session.CurrentSequence)))
            .ToArray();

        Task.WaitAll(tasks);

        session.CurrentSequence.Should().Be(100);
    }

    // ── ITerminalSessionManager interface tests ──────────────────────────

    [Test]
    public void ITerminalSessionManager_ExtendsIDisposable()
    {
        typeof(ITerminalSessionManager).Should().BeAssignableTo<IDisposable>();
    }

    [Test]
    public void TerminalSessionManager_ImplementsInterface()
    {
        typeof(TerminalSessionManager).Should().BeAssignableTo<ITerminalSessionManager>();
    }

    [Test]
    public void TerminalSessionManager_IsSealed()
    {
        typeof(TerminalSessionManager).IsSealed.Should().BeTrue();
    }

    // ── Service lifecycle tests (non-Docker paths) ─────────────────────

    [Test]
    public void Manager_TryGetSession_WithNonExistentSession_ReturnsFalse()
    {
        using var manager = CreateManager();

        var found = manager.TryGetSession("nonexistent", out var session);

        found.Should().BeFalse();
        session.Should().BeNull();
    }

    [Test]
    public void Manager_RegisterOutputCallback_DoesNotThrow()
    {
        using var manager = CreateManager();
        Func<byte[], TerminalDataDirection, CancellationToken, Task> callback =
            (_, _, _) => Task.CompletedTask;

        var action = () => manager.RegisterOutputCallback("session-1", callback);

        action.Should().NotThrow();
    }

    [Test]
    public void Manager_UnregisterOutputCallback_ForNonExistentSession_DoesNotThrow()
    {
        using var manager = CreateManager();

        var action = () => manager.UnregisterOutputCallback("nonexistent");

        action.Should().NotThrow();
    }

    [Test]
    public void Manager_RegisterOutputCallback_ReplacesExistingCallback()
    {
        using var manager = CreateManager();
        Func<byte[], TerminalDataDirection, CancellationToken, Task> first =
            (_, _, _) => Task.CompletedTask;
        Func<byte[], TerminalDataDirection, CancellationToken, Task> second =
            (_, _, _) => Task.CompletedTask;

        manager.RegisterOutputCallback("session-1", first);
        var action = () => manager.RegisterOutputCallback("session-1", second);

        action.Should().NotThrow();
    }

    [Test]
    public async Task Manager_SendInputAsync_WithNonExistentSession_ThrowsInvalidOperation()
    {
        using var manager = CreateManager();
        var data = "hello"u8.ToArray();

        var action = () => manager.SendInputAsync("nonexistent", data, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent*not found*");
    }

    [Test]
    public async Task Manager_ResizeAsync_WithNonExistentSession_ThrowsInvalidOperation()
    {
        using var manager = CreateManager();

        var action = () => manager.ResizeAsync("nonexistent", 120, 40, CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent*not found*");
    }

    [Test]
    public async Task Manager_CloseSessionAsync_WithNonExistentSession_DoesNotThrow()
    {
        using var manager = CreateManager();

        var action = () => manager.CloseSessionAsync("nonexistent", CancellationToken.None);

        await action.Should().NotThrowAsync();
    }

    [Test]
    public async Task Manager_CloseSessionAsync_CalledTwice_DoesNotThrow()
    {
        using var manager = CreateManager();

        await manager.CloseSessionAsync("session-1", CancellationToken.None);
        var action = () => manager.CloseSessionAsync("session-1", CancellationToken.None);

        await action.Should().NotThrowAsync();
    }

    [Test]
    public void Manager_Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var manager = CreateManager();

        manager.Dispose();
        var action = () => manager.Dispose();

        action.Should().NotThrow();
    }

    // ── TerminalOptions tests ────────────────────────────────────────────

    [Test]
    public void TerminalOptions_Defaults()
    {
        var options = new TerminalOptions();

        options.IdleTimeoutMinutes.Should().Be(30);
        options.ResumeGraceMinutes.Should().Be(10);
        options.MaxConcurrentSessionsPerWorker.Should().Be(20);
        options.MaxChunkBytes.Should().Be(8192);
        options.DefaultImage.Should().Be("ai-harness:latest");
    }

    [Test]
    public void TerminalOptions_SectionName_IsTerminal()
    {
        TerminalOptions.SectionName.Should().Be("Terminal");
    }

    [Test]
    public void TerminalOptions_CanBeCustomized()
    {
        var options = new TerminalOptions
        {
            IdleTimeoutMinutes = 60,
            ResumeGraceMinutes = 5,
            MaxConcurrentSessionsPerWorker = 10,
            MaxChunkBytes = 16384,
            DefaultImage = "custom-image:v2"
        };

        options.IdleTimeoutMinutes.Should().Be(60);
        options.ResumeGraceMinutes.Should().Be(5);
        options.MaxConcurrentSessionsPerWorker.Should().Be(10);
        options.MaxChunkBytes.Should().Be(16384);
        options.DefaultImage.Should().Be("custom-image:v2");
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static TerminalSessionManager CreateManager(int maxSessions = 20)
    {
        var terminalOptions = Options.Create(new TerminalOptions
        {
            MaxConcurrentSessionsPerWorker = maxSessions,
        });
        var workerOptions = Options.Create(new WorkerOptions());
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<TerminalSessionManager>>();
        return new TerminalSessionManager(terminalOptions, workerOptions, logger.Object);
    }
}
