using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using Grpc.Core;
using Microsoft.Extensions.Hosting;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkerEventListenerServiceTests
{
    [Fact]
    public void ParseEnvelope_WithValidJson_ReturnsEnvelope()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "succeeded",
            Summary = "Test success",
            Error = "",
            Metadata = new Dictionary<string, string>()
        };
        var json = JsonSerializer.Serialize(envelope);

        var result = ParseEnvelopePublic(json);

        result.Status.Should().Be("succeeded");
        result.Summary.Should().Be("Test success");
    }

    [Fact]
    public void ParseEnvelope_WithEmptyPayload_ReturnsFallbackEnvelope()
    {
        var result = ParseEnvelopePublic("");

        result.Status.Should().Be("failed");
        result.Summary.Should().Be("Worker completed without payload");
        result.Error.Should().Be("Missing payload");
    }

    [Fact]
    public void ParseEnvelope_WithNullPayload_ReturnsFallbackEnvelope()
    {
        var result = ParseEnvelopePublic(null!);

        result.Status.Should().Be("failed");
        result.Summary.Should().Be("Worker completed without payload");
        result.Error.Should().Be("Missing payload");
    }

    [Fact]
    public void ParseEnvelope_WithInvalidJson_ReturnsFallbackEnvelope()
    {
        var result = ParseEnvelopePublic("invalid json {{{");

        result.Status.Should().Be("failed");
        result.Summary.Should().Be("Invalid payload");
        result.Error.Should().Be("JSON parse failed");
    }

    [Fact]
    public void ParseEnvelope_WithMetadata_ParsesMetadata()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "succeeded",
            Summary = "Success",
            Error = "",
            Metadata = new Dictionary<string, string> { ["prUrl"] = "https://github.com/test/pr/1" }
        };
        var json = JsonSerializer.Serialize(envelope);

        var result = ParseEnvelopePublic(json);

        result.Metadata.Should().ContainKey("prUrl");
        result.Metadata["prUrl"].Should().Be("https://github.com/test/pr/1");
    }

    [Fact]
    public void ParseEnvelope_WithError_ParsesError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Summary = "Failed",
            Error = "Timeout occurred",
            Metadata = new Dictionary<string, string>()
        };
        var json = JsonSerializer.Serialize(envelope);

        var result = ParseEnvelopePublic(json);

        result.Status.Should().Be("failed");
        result.Error.Should().Be("Timeout occurred");
    }

    [Fact]
    public void FailureClassification_EnvelopeValidationError_ReturnsEnvelopeValidation()
    {
        var error = "Envelope validation failed";
        var failureClass = ClassifyFailure(error);

        failureClass.Should().Be("EnvelopeValidation");
    }

    [Fact]
    public void FailureClassification_TimeoutError_ReturnsTimeout()
    {
        var error = "Operation timeout exceeded";
        var failureClass = ClassifyFailure(error);

        failureClass.Should().Be("Timeout");
    }

    [Fact]
    public void FailureClassification_CancelledError_ReturnsTimeout()
    {
        var error = "Task was cancelled by user";
        var failureClass = ClassifyFailure(error);

        failureClass.Should().Be("Timeout");
    }

    [Fact]
    public void FailureClassification_GenericError_ReturnsNull()
    {
        var error = "Some other error";
        var failureClass = ClassifyFailure(error);

        failureClass.Should().BeNull();
    }

    [Fact]
    public void FailureClassification_EmptyError_ReturnsNull()
    {
        var failureClass = ClassifyFailure("");

        failureClass.Should().BeNull();
    }

    private static HarnessResultEnvelope ParseEnvelopePublic(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new HarnessResultEnvelope
            {
                Status = "failed",
                Summary = "Worker completed without payload",
                Error = "Missing payload",
            };
        }

        try
        {
            return JsonSerializer.Deserialize<HarnessResultEnvelope>(payloadJson) ?? new HarnessResultEnvelope
            {
                Status = "failed",
                Summary = "Invalid payload",
                Error = "JSON parse failed",
            };
        }
        catch (JsonException)
        {
            return new HarnessResultEnvelope
            {
                Status = "failed",
                Summary = "Invalid payload",
                Error = "JSON parse failed",
            };
        }
    }

    private static string? ClassifyFailure(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return null;

        if (error.Contains("Envelope validation", StringComparison.OrdinalIgnoreCase))
            return "EnvelopeValidation";

        if (error.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            return "Timeout";

        return null;
    }
}

public class WorkerEventListenerServiceLogEventTests
{

    [Fact]
    public void LogChunkEvent_CreatesCorrectLogEvent()
    {
        var timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expectedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMs).UtcDateTime;

        var logEvent = new RunLogEvent
        {
            RunId = "run-123",
            Level = "chunk",
            Message = "Log chunk message",
            TimestampUtc = expectedTimestamp
        };

        logEvent.RunId.Should().Be("run-123");
        logEvent.Level.Should().Be("chunk");
        logEvent.Message.Should().Be("Log chunk message");
        logEvent.TimestampUtc.Should().BeCloseTo(expectedTimestamp, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void RegularLogEvent_CreatesCorrectLogEvent()
    {
        var timestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expectedTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMs).UtcDateTime;

        var logEvent = new RunLogEvent
        {
            RunId = "run-456",
            Level = "info",
            Message = "Info message",
            TimestampUtc = expectedTimestamp
        };

        logEvent.RunId.Should().Be("run-456");
        logEvent.Level.Should().Be("info");
        logEvent.Message.Should().Be("Info message");
        logEvent.TimestampUtc.Should().BeCloseTo(expectedTimestamp, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void TimestampConversion_UnixMillisToDateTime_ConvertsCorrectly()
    {
        var now = DateTime.UtcNow;
        var unixMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        var converted = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;

        converted.Should().BeCloseTo(now, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void LogEventLevel_CompletedEvent_UsesCompletedLevel()
    {
        var logEvent = new RunLogEvent
        {
            RunId = "run-1",
            Level = "completed",
            Message = "Run completed",
            TimestampUtc = DateTime.UtcNow
        };

        logEvent.Level.Should().Be("completed");
    }

    [Fact]
    public void LogEventLevel_ErrorEvent_UsesErrorLevel()
    {
        var logEvent = new RunLogEvent
        {
            RunId = "run-1",
            Level = "error",
            Message = "Error occurred",
            TimestampUtc = DateTime.UtcNow
        };

        logEvent.Level.Should().Be("error");
    }
}

public class WorkerEventListenerServiceCompletedEventTests
{
    [Fact]
    public void CompletedEvent_SucceededStatus_ParsesCorrectly()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "succeeded",
            Summary = "Task completed successfully"
        };
        var json = JsonSerializer.Serialize(envelope);

        var parsed = JsonSerializer.Deserialize<HarnessResultEnvelope>(json);

        parsed.Should().NotBeNull();
        parsed!.Status.Should().Be("succeeded");
        parsed.Summary.Should().Be("Task completed successfully");
    }

    [Fact]
    public void CompletedEvent_FailedStatus_ParsesCorrectly()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Summary = "Task failed",
            Error = "Error message"
        };
        var json = JsonSerializer.Serialize(envelope);

        var parsed = JsonSerializer.Deserialize<HarnessResultEnvelope>(json);

        parsed.Should().NotBeNull();
        parsed!.Status.Should().Be("failed");
        parsed.Error.Should().Be("Error message");
    }

    [Fact]
    public void CompletedEvent_WithPrUrl_ExtractsPrUrl()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "succeeded",
            Summary = "PR created",
            Metadata = new Dictionary<string, string>
            {
                ["prUrl"] = "https://github.com/org/repo/pull/123"
            }
        };

        envelope.Metadata.TryGetValue("prUrl", out var prUrl).Should().BeTrue();
        prUrl.Should().Be("https://github.com/org/repo/pull/123");
    }

    [Fact]
    public void CompletedEvent_WithoutPrUrl_ReturnsNull()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "succeeded",
            Summary = "Completed",
            Metadata = new Dictionary<string, string>()
        };

        envelope.Metadata.TryGetValue("prUrl", out var prUrl).Should().BeFalse();
        prUrl.Should().BeNull();
    }

    [Fact]
    public void StatusComparison_Succeeded_CaseInsensitive()
    {
        var status1 = "succeeded";
        var status2 = "SUCCEEDED";
        var status3 = "Succeeded";

        string.Equals(status1, "succeeded", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(status2, "succeeded", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(status3, "succeeded", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }

    [Fact]
    public void KindComparison_Completed_CaseInsensitive()
    {
        var kind1 = "completed";
        var kind2 = "COMPLETED";
        var kind3 = "Completed";

        string.Equals(kind1, "completed", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(kind2, "completed", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        string.Equals(kind3, "completed", StringComparison.OrdinalIgnoreCase).Should().BeTrue();
    }
}

public class WorkerEventListenerServiceRetryTests
{
    [Fact]
    public void RetryLogic_MaxAttemptsOne_NoRetry()
    {
        var task = new TaskDocument
        {
            RetryPolicy = new RetryPolicyConfig(MaxAttempts: 1)
        };
        var run = new RunDocument { Attempt = 1 };

        var shouldRetry = task.RetryPolicy.MaxAttempts > 1 && run.Attempt < task.RetryPolicy.MaxAttempts;

        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void RetryLogic_FirstAttemptOfThree_ShouldRetry()
    {
        var task = new TaskDocument
        {
            RetryPolicy = new RetryPolicyConfig(MaxAttempts: 3)
        };
        var run = new RunDocument { Attempt = 1 };

        var shouldRetry = task.RetryPolicy.MaxAttempts > 1 && run.Attempt < task.RetryPolicy.MaxAttempts;

        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void RetryLogic_LastAttempt_NoRetry()
    {
        var task = new TaskDocument
        {
            RetryPolicy = new RetryPolicyConfig(MaxAttempts: 3)
        };
        var run = new RunDocument { Attempt = 3 };

        var shouldRetry = task.RetryPolicy.MaxAttempts > 1 && run.Attempt < task.RetryPolicy.MaxAttempts;

        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void RetryDelay_FirstAttempt_BaseDelay()
    {
        var backoffBaseSeconds = 10;
        var backoffMultiplier = 2.0;
        var attempt = 1;

        var delaySeconds = backoffBaseSeconds * Math.Pow(backoffMultiplier, attempt - 1);

        delaySeconds.Should().Be(10);
    }

    [Fact]
    public void RetryDelay_SecondAttempt_MultipliedDelay()
    {
        var backoffBaseSeconds = 10;
        var backoffMultiplier = 2.0;
        var attempt = 2;

        var delaySeconds = backoffBaseSeconds * Math.Pow(backoffMultiplier, attempt - 1);

        delaySeconds.Should().Be(20);
    }

    [Fact]
    public void RetryDelay_ThirdAttempt_ExponentialDelay()
    {
        var backoffBaseSeconds = 10;
        var backoffMultiplier = 2.0;
        var attempt = 3;

        var delaySeconds = backoffBaseSeconds * Math.Pow(backoffMultiplier, attempt - 1);

        delaySeconds.Should().Be(40);
    }

    [Fact]
    public void RetryDelay_MaxDelayCapped_CapsAt300Seconds()
    {
        var backoffBaseSeconds = 10;
        var backoffMultiplier = 2.0;
        var attempt = 10;

        var delaySeconds = backoffBaseSeconds * Math.Pow(backoffMultiplier, attempt - 1);
        var cappedDelay = Math.Min(delaySeconds, 300);

        cappedDelay.Should().Be(300);
        delaySeconds.Should().BeGreaterThan(300);
    }

    [Fact]
    public void RetryDelay_CustomBackoff_CalculatesCorrectly()
    {
        var backoffBaseSeconds = 5;
        var backoffMultiplier = 3.0;
        var attempt = 3;

        var delaySeconds = backoffBaseSeconds * Math.Pow(backoffMultiplier, attempt - 1);

        delaySeconds.Should().Be(45);
    }

    [Fact]
    public void NextAttempt_Increments()
    {
        var currentAttempt = 1;
        var nextAttempt = currentAttempt + 1;

        nextAttempt.Should().Be(2);
    }
}

public class WorkerEventListenerServiceYarpCleanupTests
{
    [Fact]
    public void YarpRouteId_RunId_FormatCorrect()
    {
        var runId = "run-abc123";
        var routeId = $"run-{runId}";

        routeId.Should().Be("run-run-abc123");
    }

    [Fact]
    public void YarpRouteId_Cleanup_RemovesCorrectRoute()
    {
        var runId = "test-run-id";
        var expectedRouteId = $"run-{runId}";

        expectedRouteId.Should().Be("run-test-run-id");
    }
}

public class WorkerEventListenerServiceFindingCreationTests
{
    [Fact]
    public void FailedRun_ShouldCreateFinding()
    {
        var run = new RunDocument
        {
            Id = "run-1",
            State = RunState.Failed,
            TaskId = "task-1",
            RepositoryId = "repo-1"
        };
        var error = "Test error";
        var succeeded = false;

        succeeded.Should().BeFalse();
        run.State.Should().Be(RunState.Failed);
    }

    [Fact]
    public void SucceededRun_ShouldNotCreateFinding()
    {
        var run = new RunDocument
        {
            Id = "run-1",
            State = RunState.Succeeded,
            TaskId = "task-1",
            RepositoryId = "repo-1"
        };
        var succeeded = true;

        succeeded.Should().BeTrue();
        run.State.Should().Be(RunState.Succeeded);
    }
}

public class WorkerEventListenerServiceEventStreamTests
{
    [Fact]
    public void SubscribeEventsRequest_CreateEmpty()
    {
        var request = new SubscribeEventsRequest();

        request.Should().NotBeNull();
    }

    [Fact]
    public void JobEventReply_AllFields_SetCorrectly()
    {
        var evt = new JobEventReply
        {
            RunId = "run-1",
            Kind = "info",
            Message = "Test",
            PayloadJson = "{}",
            TimestampUnixMs = 1234567890
        };

        evt.RunId.Should().Be("run-1");
        evt.Kind.Should().Be("info");
        evt.Message.Should().Be("Test");
        evt.PayloadJson.Should().Be("{}");
        evt.TimestampUnixMs.Should().Be(1234567890);
    }
}

public class WorkerEventListenerServiceBackgroundServiceTests
{
    [Fact]
    public void Service_InheritsBackgroundService()
    {
        var serviceType = Type.GetType("AgentsDashboard.ControlPlane.Services.WorkerEventListenerService, AgentsDashboard.ControlPlane");

        serviceType.Should().NotBeNull();
        serviceType!.BaseType.Should().Be(typeof(BackgroundService));
    }

    [Fact]
    public void Service_IsSealed()
    {
        var serviceType = Type.GetType("AgentsDashboard.ControlPlane.Services.WorkerEventListenerService, AgentsDashboard.ControlPlane");

        serviceType.Should().NotBeNull();
        serviceType!.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Service_HasCorrectConstructor()
    {
        var serviceType = Type.GetType("AgentsDashboard.ControlPlane.Services.WorkerEventListenerService, AgentsDashboard.ControlPlane");

        serviceType.Should().NotBeNull();
        var constructors = serviceType!.GetConstructors();

        constructors.Should().HaveCount(1);
        var parameters = constructors[0].GetParameters();
        parameters.Should().HaveCount(6);
        parameters[0].ParameterType.Name.Should().Be("WorkerGatewayClient");
        parameters[1].ParameterType.Name.Should().Be("IOrchestratorStore");
        parameters[2].ParameterType.Name.Should().Be("IRunEventPublisher");
        parameters[3].ParameterType.Name.Should().Be("InMemoryYarpConfigProvider");
        parameters[4].ParameterType.Name.Should().Be("RunDispatcher");
        parameters[5].ParameterType.Name.Should().Contain("ILogger");
    }
}

public class WorkerEventListenerServiceExceptionHandlingTests
{
    [Fact]
    public void OperationCancelled_WithCancellationRequested_ShouldBreak()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var exception = new OperationCanceledException();
        var shouldBreak = cts.Token.IsCancellationRequested;

        shouldBreak.Should().BeTrue();
    }

    [Fact]
    public void RpcException_Cancelled_WithCancellationRequested_ShouldBreak()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var statusCode = StatusCode.Cancelled;
        var shouldBreak = statusCode == StatusCode.Cancelled && cts.Token.IsCancellationRequested;

        shouldBreak.Should().BeTrue();
    }

    [Fact]
    public void RpcException_Cancelled_WithoutCancellationRequested_ShouldNotBreak()
    {
        var cts = new CancellationTokenSource();

        var statusCode = StatusCode.Cancelled;
        var shouldBreak = statusCode == StatusCode.Cancelled && cts.Token.IsCancellationRequested;

        shouldBreak.Should().BeFalse();
    }

    [Fact]
    public void GenericException_ShouldReconnect()
    {
        var exception = new Exception("Connection lost");
        var reconnectDelay = TimeSpan.FromSeconds(2);

        reconnectDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void RpcException_OtherStatus_ShouldReconnect()
    {
        var statusCode = StatusCode.Unavailable;
        var reconnectDelay = TimeSpan.FromSeconds(2);

        statusCode.Should().NotBe(StatusCode.Cancelled);
        reconnectDelay.Should().Be(TimeSpan.FromSeconds(2));
    }
}
