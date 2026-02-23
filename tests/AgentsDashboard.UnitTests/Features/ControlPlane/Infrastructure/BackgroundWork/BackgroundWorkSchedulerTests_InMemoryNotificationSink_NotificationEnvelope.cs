using System.Collections.Concurrent;

using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class BackgroundWorkSchedulerTests
{
    private sealed partial class InMemoryNotificationSink : INotificationSink
    {
        public sealed record NotificationEnvelope(
            string Title,
            string? Message,
            NotificationSeverity Severity,
            NotificationSource Source,
            string? CorrelationId);

    }
}
