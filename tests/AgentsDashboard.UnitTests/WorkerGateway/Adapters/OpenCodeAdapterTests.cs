using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class OpenCodeAdapterTests
{
    private readonly OpenCodeAdapter _adapter;

    public OpenCodeAdapterTests()
    {
        var options = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(options);
        _adapter = new OpenCodeAdapter(options, redactor, NullLogger<OpenCodeAdapter>.Instance);
    }

    [Fact]
    public void HarnessName_ReturnsOpenCode()
    {
        _adapter.HarnessName.Should().Be("opencode");
    }

    [Fact]
    public void PrepareContext_SetsDefaultValues()
    {
        var request = CreateRequest();

        var context = _adapter.PrepareContext(request);

        context.RunId.Should().Be("test-run-id");
        context.Harness.Should().Be("opencode");
        context.Prompt.Should().Be("test prompt");
        context.Command.Should().Be("echo test");
        context.TimeoutSeconds.Should().Be(600);
    }

    [Fact]
    public void BuildCommand_IncludesRequiredEnvVariables()
    {
        var request = CreateRequest();
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("-e");
        command.Arguments.Should().Contain("OPENCODE_FORMAT=json");
        command.Arguments.Should().Contain("OPENCODE_OUTPUT_ENVELOPE=true");
        command.Arguments.Should().Contain("PROMPT=test prompt");
        command.Arguments.Should().Contain("HARNESS=opencode");
    }

    [Fact]
    public void BuildCommand_IncludesOptionalEnvVariables_WhenPresent()
    {
        var request = CreateRequest();
        request.Env["OPENCODE_MODEL"] = "claude-3";
        request.Env["OPENCODE_PROVIDER"] = "anthropic";
        request.Env["OPENCODE_TEMPERATURE"] = "0.7";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("OPENCODE_MODEL=claude-3");
        command.Arguments.Should().Contain("OPENCODE_PROVIDER=anthropic");
        command.Arguments.Should().Contain("OPENCODE_TEMPERATURE=0.7");
    }

    [Fact]
    public void ClassifyFailure_ReturnsSuccess_WhenEnvelopeSucceeded()
    {
        var envelope = new HarnessResultEnvelope { Status = "succeeded" };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.None);
        classification.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ClassifyFailure_ReturnsResourceExhausted_WhenContextLimitExceeded()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "opencode context window token limit exceeded"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ResourceExhausted);
        classification.Reason.Should().Be("Context limit exceeded");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
        classification.RemediationHints.Should().Contain("Reduce context size");
        classification.RemediationHints.Should().Contain("Split into smaller tasks");
        classification.RemediationHints.Should().Contain("Use context compression");
    }

    [Fact]
    public void ClassifyFailure_ReturnsPermissionDenied_WhenFileEditFailed()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "editor file edit write failed"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.PermissionDenied);
        classification.Reason.Should().Be("File edit failed");
        classification.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ClassifyFailure_ReturnsInternalError_WhenTerminalFailed()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "terminal shell command execution failed"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InternalError);
        classification.Reason.Should().Be("Terminal command failed");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(10);
    }

    [Fact]
    public void ClassifyFailure_ReturnsConfigurationError_WhenProviderUnavailable()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "provider model not available unsupported"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ConfigurationError);
        classification.Reason.Should().Be("Provider/model configuration error");
        classification.IsRetryable.Should().BeFalse();
    }

    [Theory]
    [InlineData("unauthorized", FailureClass.AuthenticationError)]
    [InlineData("rate limit", FailureClass.RateLimitExceeded)]
    [InlineData("timeout", FailureClass.Timeout)]
    [InlineData("not found", FailureClass.NotFound)]
    [InlineData("network error", FailureClass.NetworkError)]
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

    [Fact]
    public void MapArtifacts_ReturnsEmptyList_WhenNoArtifacts()
    {
        var envelope = new HarnessResultEnvelope();

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().BeEmpty();
    }

    [Fact]
    public void MapArtifacts_MapsArtifactPaths()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts = ["/workspace/file1.txt", "/workspace/file2.md"]
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Name == "file1.txt");
        artifacts.Should().Contain(a => a.Name == "file2.md");
    }

    [Fact]
    public void MapArtifacts_IncludesChangedFiles_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["changedFiles"] = "/src/file1.cs, /src/file2.cs" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Path == "/src/file1.cs");
        artifacts.Should().Contain(a => a.Path == "/src/file2.cs");
    }

    [Fact]
    public void MapArtifacts_IncludesLogFile_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["logFile"] = "/workspace/opencode.log" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "opencode.log" && a.Type == "log");
    }

    [Fact]
    public void MapArtifacts_DoesNotDuplicateChangedFiles_WhenAlreadyInArtifacts()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts = ["/src/file1.cs"],
            Metadata = new Dictionary<string, string> { ["changedFiles"] = "/src/file1.cs, /src/file2.cs" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Count(a => a.Path == "/src/file1.cs").Should().Be(1);
        artifacts.Should().Contain(a => a.Path == "/src/file2.cs");
    }

    [Fact]
    public void ParseEnvelope_ParsesValidJsonEnvelope()
    {
        var stdout = """{"status":"succeeded","summary":"All done"}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Status.Should().Be("succeeded");
        envelope.Summary.Should().Be("All done");
    }

    [Fact]
    public void ParseEnvelope_CreatesFallback_WhenInvalidJson()
    {
        var stdout = "raw output";

        var envelope = _adapter.ParseEnvelope(stdout, "error message", 1);

        envelope.Status.Should().Be("failed");
        envelope.Error.Should().Be("error message");
        envelope.Metadata["stdout"].Should().Be("raw output");
    }

    private static DispatchJobRequest CreateRequest()
    {
        var request = new DispatchJobRequest
        {
            RunId = "test-run-id",
            Harness = "opencode",
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
