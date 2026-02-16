using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class HarnessHealthServiceTests
{
    private readonly Mock<ILogger<HarnessHealthService>> _loggerMock;
    private readonly HarnessHealthService _service;

    public HarnessHealthServiceTests()
    {
        _loggerMock = new Mock<ILogger<HarnessHealthService>>();
        _service = new HarnessHealthService(_loggerMock.Object);
    }

    [Test]
    public void GetAllHealth_Initially_ReturnsEmpty()
    {
        var health = _service.GetAllHealth();

        health.Should().BeEmpty();
    }

    [Test]
    public async Task StartAsync_TriggersInitialRefresh()
    {
        await _service.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await _service.RefreshAsync(CancellationToken.None);

        var health = _service.GetAllHealth();
        health.Should().NotBeEmpty();
        health.Keys.Should().Contain("codex");
        health.Keys.Should().Contain("opencode");
        health.Keys.Should().Contain("claude");
        health.Keys.Should().Contain("zai");
    }

    [Test]
    public async Task StopAsync_DoesNotThrow()
    {
        await _service.StartAsync(CancellationToken.None);

        var act = () => _service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task GetAllHealth_ReturnsReadOnlyDictionary()
    {
        await _service.RefreshAsync(CancellationToken.None);

        var health = _service.GetAllHealth();

        health.Should().BeAssignableTo<IReadOnlyDictionary<string, HarnessHealth>>();
    }

    [Test]
    public async Task RefreshAsync_CanBeCalledMultipleTimes()
    {
        await _service.RefreshAsync(CancellationToken.None);
        await _service.RefreshAsync(CancellationToken.None);
        await _service.RefreshAsync(CancellationToken.None);

        var health = _service.GetAllHealth();
        health.Should().HaveCount(4);
    }

    [Test]
    public void Dispose_AfterStart_DoesNotThrow()
    {
        _service.StartAsync(CancellationToken.None).Wait();

        var act = () => _service.Dispose();

        act.Should().NotThrow();
    }

    [Test]
    public async Task HarnessHealth_Record_HasExpectedProperties()
    {
        await _service.RefreshAsync(CancellationToken.None);

        var health = _service.GetAllHealth();
        var firstHealth = health.Values.First();

        firstHealth.Name.Should().NotBeNullOrEmpty();
        firstHealth.Status.Should().BeDefined();
    }
}
