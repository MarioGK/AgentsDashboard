using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class WorkflowExecutorTests
{
        private sealed class StaticTimeProvider(DateTimeOffset initialTime) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => initialTime;
        }

}
