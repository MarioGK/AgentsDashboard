using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class ClaudeCodeAdapterTests
{
    private readonly ClaudeCodeAdapter _adapter;

    public ClaudeCodeAdapterTests()
    {
        var options = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(options);
        _adapter = new ClaudeCodeAdapter(options, redactor, NullLogger<ClaudeCodeAdapter>.Instance);
    }

    [Test]
    public void HarnessName_ReturnsClaudeCode()
    {
        _adapter.HarnessName.Should().Be("claude-code");
    }

    [Test]
    public void PrepareContext_SetsDefaultValues()
    {
        var request = CreateRequest();

        var context = _adapter.PrepareContext(request);

        context.RunId.Should().Be("test-run-id");
        context.Harness.Should().Be("claude-code");
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
        command.Arguments.Should().Contain("HARNESS=claude-code");
    }

    [Test]
    public void BuildCommand_IncludesOptionalEnvVariables_WhenPresent()
    {
        var request = CreateRequest();
        request.EnvironmentVars!["CLAUDE_MODEL"] = "claude-3-opus";
        request.EnvironmentVars!["ANTHROPIC_MODEL"] = "claude-3-opus-20240229";
        request.EnvironmentVars!["CLAUDE_MAX_THINKING_TOKENS"] = "10000";
        request.EnvironmentVars!["CLAUDE_MCP_SERVERS"] = "server1,server2";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("CLAUDE_MODEL=claude-3-opus");
        command.Arguments.Should().Contain("ANTHROPIC_MODEL=claude-3-opus-20240229");
        command.Arguments.Should().Contain("CLAUDE_MAX_THINKING_TOKENS=10000");
        command.Arguments.Should().Contain("CLAUDE_MCP_SERVERS=server1,server2");
    }

    [Test]
    public void BuildCommand_IncludesSkipPermissions_WhenSetToTrue()
    {
        var request = CreateRequest();
        request.EnvironmentVars!["CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS"] = "true";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS=true");
    }

    [Test]
    [Arguments("false")]
    [Arguments("FALSE")]
    [Arguments("")]
    public void BuildCommand_SkipsSkipPermissions_WhenNotTrue(string value)
    {
        var request = CreateRequest();
        request.EnvironmentVars!["CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS"] = value;
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().NotContain("CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS=true");
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
        classification.Reason.Should().Be("Claude service overloaded");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(60);
    }

    [Test]
    public void ClassifyFailure_ReturnsResourceExhausted_WhenPromptTooLong()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "prompt too long context length max tokens exceeded"
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
            Error = "content policy safety refused harmful content"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InvalidInput);
        classification.Reason.Should().Be("Content policy violation");
        classification.IsRetryable.Should().BeFalse();
    }

    [Test]
    public void ClassifyFailure_ReturnsConfigurationError_WhenMcpToolError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "mcp tool server connection failed"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ConfigurationError);
        classification.Reason.Should().Be("MCP tool error");
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
            Error = "claude code cli binary error"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InternalError);
        classification.Reason.Should().Be("Claude Code CLI error");
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
            Artifacts = ["/workspace/file1.txt", "/workspace/file2.json"]
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Name == "file1.txt");
        artifacts.Should().Contain(a => a.Name == "file2.json");
    }

    [Test]
    public void MapArtifacts_IncludesEditedFiles_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["editedFiles"] = "/src/code.cs, /src/test.cs" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Path == "/src/code.cs");
        artifacts.Should().Contain(a => a.Path == "/src/test.cs");
    }

    [Test]
    public void MapArtifacts_IncludesThinkingOutput_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["thinkingOutput"] = "/workspace/thinking.txt" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "thinking.md" && a.Type == "markdown");
    }

    [Test]
    public void MapArtifacts_IncludesToolUseLog_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["toolUseLog"] = "/workspace/toollog.json" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "toollog.json" && a.Type == "json");
    }

    [Test]
    public void MapArtifacts_DoesNotDuplicateEditedFiles_WhenAlreadyInArtifacts()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts = ["/src/file1.cs"],
            Metadata = new Dictionary<string, string> { ["editedFiles"] = "/src/file1.cs, /src/file2.cs" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Count(a => a.Path == "/src/file1.cs").Should().Be(1);
        artifacts.Should().Contain(a => a.Path == "/src/file2.cs");
    }

    [Test]
    public void ParseEnvelope_ParsesValidJsonEnvelope()
    {
        var stdout = """{"status":"succeeded","summary":"Task done"}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Status.Should().Be("succeeded");
        envelope.Summary.Should().Be("Task done");
    }

    [Test]
    public void ParseEnvelope_AddsHasThinking_WhenThinkingPresent()
    {
        var stdout = """{"status":"succeeded","metadata":{"thinking":"some thinking content"}}""";

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
        var stdout = """{"status":"succeeded","metadata":{"toolCalls":"call1,call2,call3"}}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Metadata["toolCallCount"].Should().Be("3");
    }

    [Test]
    public void ParseEnvelope_CreatesFallback_WhenInvalidJson()
    {
        var stdout = "plain text";

        var envelope = _adapter.ParseEnvelope(stdout, "error", 1);

        envelope.Status.Should().Be("failed");
        envelope.Error.Should().Be("error");
        envelope.Metadata["stdout"].Should().Be("plain text");
    }

    private static DispatchJobRequest CreateRequest()
    {
        var request = new DispatchJobRequest
        {
            RunId = "test-run-id",
            ProjectId = "proj-1",
            RepositoryId = "repo-1",
            TaskId = "task-1",
            HarnessType = "claude-code",
            ImageTag = "latest",
            CloneUrl = "https://github.com/test/repo.git",
            Instruction = "test prompt",
            CustomArgs = "echo test",
            TimeoutSeconds = 0,
            SandboxProfileCpuLimit = 0,
            SandboxProfileMemoryLimit = null,
            SandboxProfileNetworkDisabled = false,
            SandboxProfileReadOnlyRootFs = false,
            EnvironmentVars = new Dictionary<string, string>(),
            ContainerLabels = new Dictionary<string, string>()
        };
        return request;
    }
}
