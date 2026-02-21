using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class TaskRetentionCleanupServiceTests
{
        private sealed class StaticTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

}
