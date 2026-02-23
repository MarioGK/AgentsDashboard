using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class OrchestratorRuntimeSettingsProviderTests
{
    [Test]
    public async Task GetAsync_WhenMinWorkersIsUnset_UsesOne()
    {
        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(x => x.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsDocument
            {
                Orchestrator = new OrchestratorSettings
                {
                    MinWorkers = 0,
                    MaxWorkers = 4
                }
            });

        var provider = new OrchestratorRuntimeSettingsProvider(
            store.Object,
            Options.Create(new OrchestratorOptions()));

        var runtime = await provider.GetAsync(CancellationToken.None);

        runtime.MinWorkers.Should().Be(1);
        runtime.MinWorkers.Should().BeGreaterThan(0);
        runtime.MaxWorkers.Should().Be(4);
    }

    [Test]
    public async Task GetAsync_WhenMinWorkersConfigured_UsesConfiguredValue()
    {
        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(x => x.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsDocument
            {
                Orchestrator = new OrchestratorSettings
                {
                    MinWorkers = 3,
                    MaxWorkers = 5
                }
            });

        var provider = new OrchestratorRuntimeSettingsProvider(
            store.Object,
            Options.Create(new OrchestratorOptions()));

        var runtime = await provider.GetAsync(CancellationToken.None);

        runtime.MinWorkers.Should().Be(3);
        runtime.MaxWorkers.Should().Be(5);
    }
}
