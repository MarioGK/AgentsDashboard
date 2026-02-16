using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class ZaiAdapterTests
{
    private readonly ZaiAdapter _adapter;

    public ZaiAdapterTests()
    {
        var options = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(options);
        _adapter = new ZaiAdapter(options, redactor, NullLogger<ZaiAdapter>.Instance);
    }

    [Test]
    public void HarnessName_ReturnsZai()
    {
        _adapter.HarnessName.Should().Be("zai");
    }

    [Test]
    public void PrepareContext_SetsDefaultValues()
    {
        var request = CreateRequest();

        var context = _adapter.PrepareContext(request);

        context.RunId.Should().Be("test-run-id");
        context.Harness.Should().Be("zai");
        context.Prompt.Should().Be("test prompt");
        context.Command.Should().Be("echo test");
        context.TimeoutSeconds.Should().Be(600);
    }

    [Test]
    public void BuildCommand_IncludesRequiredEnvVariables()
    {
        var request = CreateRequest();
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("-e");
        command.Arguments.Should().Contain("CLAUDE_CODE_FORMAT=json");
        command.Arguments.Should().Contain("CLAUDE_OUTPUT_ENVELOPE=true");
        command.Arguments.Should().Contain("PROMPT=test prompt");
        command.Arguments.Should().Contain("HARNESS=zai");
    }

    [Test]
    public void BuildCommand_IncludesOptionalEnvVariables_WhenPresent()
    {
        var request = CreateRequest();
        request.Env["ZAI_MODEL"] = "glm-5";
        request.Env["Z_AI_API_KEY"] = "test-api-key";
        request.Env["ZAI_MAX_THINKING_TOKENS"] = "8000";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("ZAI_MODEL=glm-5");
        command.Arguments.Should().Contain("Z_AI_API_KEY=test-api-key");
        command.Arguments.Should().Contain("ZAI_MAX_THINKING_TOKENS=8000");
    }

    [Test]
    public void ClassifyFailure_ReturnsSuccess_WhenEnvelopeSucceeded()
    {
        var envelope = new HarnessResultEnvelope { Status = "succeeded" };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.None);
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void ClassifyFailure_ReturnsRateLimitExceeded_WhenServiceOverloaded()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "overloaded capacity service unavailable 503"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.RateLimitExceeded);
        classification.Reason.Should().Be("GLM-5 service overloaded");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(60);
    }

    [Test]
    public void ClassifyFailure_ReturnsResourceExhausted_WhenPromptTooLong()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "prompt too long context length max tokens input too long"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ResourceExhausted);
        classification.Reason.Should().Be("Context too long");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
    }

    [Test]
    public void ClassifyFailure_ReturnsInvalidInput_WhenContentPolicyViolation()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "content policy safety refused harmful violation"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InvalidInput);
        classification.Reason.Should().Be("Content policy violation");
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void ClassifyFailure_ReturnsConfigurationError_WhenToolError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "tool function call server connection failed"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ConfigurationError);
        classification.Reason.Should().Be("Tool error");
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void ClassifyFailure_ReturnsPermissionDenied_WhenApprovalDenied()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "permission approval denied user rejected"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.PermissionDenied);
        classification.Reason.Should().Be("Permission denied");
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void ClassifyFailure_ReturnsInternalError_WhenCliError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "zai cc-mirror cli binary installation error"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InternalError);
        classification.Reason.Should().Be("Zai CLI error");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
        classification.RemediationHints.Should().Contain(h => h.Contains("cc-mirror"));
    }

    [Test]
    public void ClassifyFailure_ReturnsConfigurationError_WhenGlmApiError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "glm-5 model api error"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ConfigurationError);
        classification.Reason.Should().Be("GLM-5 API error");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
    }

    [Test]
    [Arguments("unauthorized", FailureClass.AuthenticationError)]
    [Arguments("rate limit", FailureClass.RateLimitExceeded)]
    [Arguments("timeout", FailureClass.Timeout)]
    [Arguments("not found", FailureClass.NotFound)]
    [Arguments("network error", FailureClass.NetworkError)]
    public void ClassifyFailure_ReturnsCorrectBaseClassification(string error, FailureClass expectedClass)
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = error
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(expectedClass);
    }

    [Test]
    public void MapArtifacts_ReturnsEmptyList_WhenNoArtifacts()
    {
        var envelope = new HarnessResultEnvelope();

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().BeEmpty();
    }

    [Test]
    public void MapArtifacts_MapsArtifactPaths()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts = ["/workspace/file1.txt", "/workspace/file2.py"]
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Name == "file1.txt");
        artifacts.Should().Contain(a => a.Name == "file2.py");
    }

    [Test]
    public void MapArtifacts_IncludesEditedFiles_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["editedFiles"] = "/src/main.py, /src/utils.py" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Path == "/src/main.py");
        artifacts.Should().Contain(a => a.Path == "/src/utils.py");
    }

    [Test]
    public void MapArtifacts_IncludesThinkingOutput_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["thinkingOutput"] = "/workspace/zai_thinking.txt" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "thinking.md" && a.Type == "markdown");
    }

    [Test]
    public void MapArtifacts_IncludesToolUseLog_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["toolUseLog"] = "/workspace/tool_calls.json" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "tool_calls.json" && a.Type == "json");
    }

    [Test]
    public void MapArtifacts_DoesNotDuplicateEditedFiles_WhenAlreadyInArtifacts()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts = ["/src/file1.py"],
            Metadata = new Dictionary<string, string> { ["editedFiles"] = "/src/file1.py, /src/file2.py" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Count(a => a.Path == "/src/file1.py").Should().Be(1);
        artifacts.Should().Contain(a => a.Path == "/src/file2.py");
    }

    [Test]
    public void ParseEnvelope_ParsesValidJsonEnvelope()
    {
        var stdout = """{"status":"succeeded","summary":"GLM-5 task completed"}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Status.Should().Be("succeeded");
        envelope.Summary.Should().Be("GLM-5 task completed");
    }

    [Test]
    public void ParseEnvelope_AddsHasThinking_WhenThinkingPresent()
    {
        var stdout = """{"status":"succeeded","metadata":{"thinking":"reasoning content"}}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Metadata["hasThinking"].Should().Be("true");
    }

    [Test]
    public void ParseEnvelope_DoesNotAddHasThinking_WhenThinkingAbsent()
    {
        var stdout = """{"status":"succeeded"}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Metadata.Should().NotContainKey("hasThinking");
    }

    [Test]
    public void ParseEnvelope_AddsToolCallCount_WhenToolCallsPresent()
    {
        var stdout = """{"status":"succeeded","metadata":{"toolCalls":"read,write,execute"}}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Metadata["toolCallCount"].Should().Be("3");
    }

    [Test]
    public void ParseEnvelope_CreatesFallback_WhenInvalidJson()
    {
        var stdout = "raw zai output";

        var envelope = _adapter.ParseEnvelope(stdout, "zai error", 1);

        envelope.Status.Should().Be("failed");
        envelope.Error.Should().Be("zai error");
        envelope.Metadata["stdout"].Should().Be("raw zai output");
    }

    [Test]
    public void ClassifyFailure_UsesSummaryAsFallback_WhenErrorIsEmpty()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "",
            Summary = "timeout occurred"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.Timeout);
    }

    private static DispatchJobRequest CreateRequest()
    {
        var request = new DispatchJobRequest
        {
            RunId = "test-run-id",
            Harness = "zai",
            Command = "echo test",
            Prompt = "test prompt",
            TimeoutSeconds = 0,
            SandboxProfileCpuLimit = 0,
            SandboxProfileMemoryLimit = "",
            SandboxProfileNetworkDisabled = false,
            SandboxProfileReadOnlyRootFs = false
        };
        return request;
    }
}
