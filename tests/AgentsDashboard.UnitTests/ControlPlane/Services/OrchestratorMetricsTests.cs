using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class OrchestratorMetricsTests
{
    private readonly OrchestratorMetrics _metrics;

    public OrchestratorMetricsTests()
    {
        _metrics = new OrchestratorMetrics();
    }

    [Test]
    public void Constructor_CreatesInstance()
    {
        var metrics = new OrchestratorMetrics();

        metrics.Should().NotBeNull();
        metrics.Should().BeAssignableTo<IOrchestratorMetrics>();
    }

    [Test]
    public void RecordRunStart_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.RecordRunStart("codex", "project-1", "repo-1");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordRunStart_WithMultipleHarnesses_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordRunStart("codex", "project-1", "repo-1");
            _metrics.RecordRunStart("opencode", "project-1", "repo-1");
            _metrics.RecordRunStart("claude-code", "project-2", "repo-2");
            _metrics.RecordRunStart("zai", "project-2", "repo-2");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordRunComplete_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.RecordRunComplete("codex", "Succeeded", 120.5, "project-1", "repo-1");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordRunComplete_WithDifferentStates_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordRunComplete("codex", "Succeeded", 100.0, "project-1", "repo-1");
            _metrics.RecordRunComplete("opencode", "Failed", 50.0, "project-1", "repo-2");
            _metrics.RecordRunComplete("claude-code", "Cancelled", 10.0, "project-2", "repo-1");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordJobDispatch_WithValidHarness_DoesNotThrow()
    {
        var action = () => _metrics.RecordJobDispatch("codex");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordJobDispatch_WithMultipleHarnesses_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordJobDispatch("codex");
            _metrics.RecordJobDispatch("opencode");
            _metrics.RecordJobDispatch("claude-code");
            _metrics.RecordJobDispatch("zai");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordError_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.RecordError("TimeoutException", "execution");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordError_WithMultipleErrorTypes_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordError("TimeoutException", "execution");
            _metrics.RecordError("AuthException", "authentication");
            _metrics.RecordError("RateLimitException", "api");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void SetPendingJobs_WithValidCount_DoesNotThrow()
    {
        var action = () => _metrics.SetPendingJobs(10);

        action.Should().NotThrow();
    }

    [Test]
    public void SetPendingJobs_WithZero_DoesNotThrow()
    {
        var action = () => _metrics.SetPendingJobs(0);

        action.Should().NotThrow();
    }

    [Test]
    public void SetActiveJobs_WithValidCount_DoesNotThrow()
    {
        var action = () => _metrics.SetActiveJobs(5);

        action.Should().NotThrow();
    }

    [Test]
    public void SetQueuedRuns_WithValidCount_DoesNotThrow()
    {
        var action = () => _metrics.SetQueuedRuns(20);

        action.Should().NotThrow();
    }

    [Test]
    public void SetActiveRuns_WithValidCount_DoesNotThrow()
    {
        var action = () => _metrics.SetActiveRuns(8);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordQueueWaitTime_WithValidSeconds_DoesNotThrow()
    {
        var action = () => _metrics.RecordQueueWaitTime(5.5);

        action.Should().NotThrow();
    }

    [Test]
    public void SetWorkerSlots_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.SetWorkerSlots("worker-1", 2, 4);

        action.Should().NotThrow();
    }

    [Test]
    public void SetWorkerSlots_WithMultipleWorkers_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.SetWorkerSlots("worker-1", 2, 4);
            _metrics.SetWorkerSlots("worker-2", 3, 4);
            _metrics.SetWorkerSlots("worker-3", 0, 4);
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordStatusUpdateLatency_WithValidSeconds_DoesNotThrow()
    {
        var action = () => _metrics.RecordStatusUpdateLatency(0.5);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordProxyRequest_WithSuccessStatus_DoesNotThrow()
    {
        var action = () => _metrics.RecordProxyRequest("success", 0.1);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordProxyRequest_WithErrorStatus_DoesNotThrow()
    {
        var action = () => _metrics.RecordProxyRequest("error", 0.2);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordProxyRequest_WithMultipleStatuses_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordProxyRequest("success", 0.1);
            _metrics.RecordProxyRequest("error", 0.2);
            _metrics.RecordProxyRequest("timeout", 1.0);
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordAlertFired_WithValidRuleId_DoesNotThrow()
    {
        var action = () => _metrics.RecordAlertFired("alert-rule-1");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordWebhookDelivery_WithSuccessStatus_DoesNotThrow()
    {
        var action = () => _metrics.RecordWebhookDelivery("success");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordWebhookDelivery_WithFailedStatus_DoesNotThrow()
    {
        var action = () => _metrics.RecordWebhookDelivery("failed");

        action.Should().NotThrow();
    }

    [Test]
    public void SetSignalRConnections_WithValidCount_DoesNotThrow()
    {
        var action = () => _metrics.SetSignalRConnections(10);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordGrpcDuration_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.RecordGrpcDuration("DispatchJob", 0.05);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordGrpcDuration_WithMultipleMethods_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordGrpcDuration("DispatchJob", 0.05);
            _metrics.RecordGrpcDuration("CancelJob", 0.02);
            _metrics.RecordGrpcDuration("Heartbeat", 0.01);
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordFinding_WithValidSeverity_DoesNotThrow()
    {
        var action = () => _metrics.RecordFinding("high");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordFinding_WithMultipleSeverities_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordFinding("critical");
            _metrics.RecordFinding("high");
            _metrics.RecordFinding("medium");
            _metrics.RecordFinding("low");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void RecordArtifactGenerated_WithValidHarness_DoesNotThrow()
    {
        var action = () => _metrics.RecordArtifactGenerated("codex");

        action.Should().NotThrow();
    }

    [Test]
    public void RecordContainerMetrics_WithValidParameters_DoesNotThrow()
    {
        var action = () => _metrics.RecordContainerMetrics("codex", 25.5, 536_870_912);

        action.Should().NotThrow();
    }

    [Test]
    public void RecordContainerMetrics_WithZeroValues_DoesNotThrow()
    {
        var action = () => _metrics.RecordContainerMetrics("opencode", 0.0, 0.0);

        action.Should().NotThrow();
    }

    [Test]
    public void MultipleOperationsSequentially_DoesNotThrow()
    {
        var action = () =>
        {
            _metrics.RecordRunStart("codex", "project-1", "repo-1");
            _metrics.SetPendingJobs(5);
            _metrics.SetActiveJobs(3);
            _metrics.SetQueuedRuns(10);
            _metrics.SetActiveRuns(8);
            _metrics.RecordQueueWaitTime(2.5);
            _metrics.SetWorkerSlots("worker-1", 2, 4);
            _metrics.RecordStatusUpdateLatency(0.1);
            _metrics.RecordProxyRequest("success", 0.05);
            _metrics.RecordAlertFired("alert-1");
            _metrics.RecordWebhookDelivery("success");
            _metrics.SetSignalRConnections(5);
            _metrics.RecordGrpcDuration("DispatchJob", 0.02);
            _metrics.RecordFinding("high");
            _metrics.RecordArtifactGenerated("codex");
            _metrics.RecordContainerMetrics("codex", 50.0, 1_073_741_824);
            _metrics.RecordRunComplete("codex", "Succeeded", 120.0, "project-1", "repo-1");
        };

        action.Should().NotThrow();
    }

    [Test]
    public void Interface_HasAllRequiredMethods()
    {
        var interfaceType = typeof(IOrchestratorMetrics);
        var methods = interfaceType.GetMethods();

        methods.Should().Contain(m => m.Name == "RecordRunStart");
        methods.Should().Contain(m => m.Name == "RecordRunComplete");
        methods.Should().Contain(m => m.Name == "RecordJobDispatch");
        methods.Should().Contain(m => m.Name == "RecordError");
        methods.Should().Contain(m => m.Name == "SetPendingJobs");
        methods.Should().Contain(m => m.Name == "SetActiveJobs");
        methods.Should().Contain(m => m.Name == "SetQueuedRuns");
        methods.Should().Contain(m => m.Name == "SetActiveRuns");
        methods.Should().Contain(m => m.Name == "RecordQueueWaitTime");
        methods.Should().Contain(m => m.Name == "SetWorkerSlots");
        methods.Should().Contain(m => m.Name == "RecordStatusUpdateLatency");
        methods.Should().Contain(m => m.Name == "RecordProxyRequest");
        methods.Should().Contain(m => m.Name == "RecordAlertFired");
        methods.Should().Contain(m => m.Name == "RecordWebhookDelivery");
        methods.Should().Contain(m => m.Name == "SetSignalRConnections");
        methods.Should().Contain(m => m.Name == "RecordGrpcDuration");
        methods.Should().Contain(m => m.Name == "RecordFinding");
        methods.Should().Contain(m => m.Name == "RecordArtifactGenerated");
        methods.Should().Contain(m => m.Name == "RecordContainerMetrics");

        methods.Length.Should().Be(19);
    }
}
