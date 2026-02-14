using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Adapters;

public class CodexAdapterTests
{
    private readonly CodexAdapter _adapter;

    public CodexAdapterTests()
    {
        var options = Options.Create(new WorkerOptions());
        var redactor = new SecretRedactor(options);
        _adapter = new CodexAdapter(options, redactor, NullLogger<CodexAdapter>.Instance);
    }

    [Fact]
    public void HarnessName_ReturnsCodex()
    {
        _adapter.HarnessName.Should().Be("codex");
    }

    [Fact]
    public void PrepareContext_SetsDefaultValues()
    {
        var request = CreateRequest();

        var context = _adapter.PrepareContext(request);

        context.RunId.Should().Be("test-run-id");
        context.Harness.Should().Be("codex");
        context.Prompt.Should().Be("test prompt");
        context.Command.Should().Be("echo test");
        context.TimeoutSeconds.Should().Be(600);
        context.CpuLimit.Should().Be(1.5);
        context.MemoryLimit.Should().Be("2g");
        context.NetworkDisabled.Should().BeFalse();
        context.ReadOnlyRootFs.Should().BeFalse();
    }

    [Fact]
    public void PrepareContext_UsesCustomTimeout_WhenProvided()
    {
        var request = CreateRequest();
        request.TimeoutSeconds = 300;

        var context = _adapter.PrepareContext(request);

        context.TimeoutSeconds.Should().Be(300);
    }

    [Fact]
    public void PrepareContext_UsesCustomSandboxSettings()
    {
        var request = CreateRequest();
        request.SandboxProfileCpuLimit = 2.0;
        request.SandboxProfileMemoryLimit = "4g";
        request.SandboxProfileNetworkDisabled = true;
        request.SandboxProfileReadOnlyRootFs = true;

        var context = _adapter.PrepareContext(request);

        context.CpuLimit.Should().Be(2.0);
        context.MemoryLimit.Should().Be("4g");
        context.NetworkDisabled.Should().BeTrue();
        context.ReadOnlyRootFs.Should().BeTrue();
    }

    [Fact]
    public void BuildCommand_IncludesRequiredEnvVariables()
    {
        var request = CreateRequest();
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("-e");
        command.Arguments.Should().Contain("CODEX_FORMAT=json");
        command.Arguments.Should().Contain("CODEX_OUTPUT_ENVELOPE=true");
        command.Arguments.Should().Contain("PROMPT=test prompt");
        command.Arguments.Should().Contain("HARNESS=codex");
    }

    [Fact]
    public void BuildCommand_IncludesOptionalEnvVariables_WhenPresent()
    {
        var request = CreateRequest();
        request.Env["CODEX_MODEL"] = "gpt-4";
        request.Env["CODEX_MAX_TOKENS"] = "4096";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("CODEX_MODEL=gpt-4");
        command.Arguments.Should().Contain("CODEX_MAX_TOKENS=4096");
    }

    [Fact]
    public void BuildCommand_IncludesContainerLabels()
    {
        var request = CreateRequest();
        request.ContainerLabels["app"] = "test-app";
        request.ContainerLabels["env"] = "test";
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("--label");
        command.Arguments.Should().Contain("app=test-app");
        command.Arguments.Should().Contain("env=test");
    }

    [Fact]
    public void BuildCommand_IncludesNetworkDisabled_WhenSet()
    {
        var request = CreateRequest();
        request.SandboxProfileNetworkDisabled = true;
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("--network");
        command.Arguments.Should().Contain("none");
    }

    [Fact]
    public void BuildCommand_IncludesReadOnlyRootFs_WhenSet()
    {
        var request = CreateRequest();
        request.SandboxProfileReadOnlyRootFs = true;
        var context = _adapter.PrepareContext(request);

        var command = _adapter.BuildCommand(context);

        command.Arguments.Should().Contain("--read-only");
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
    public void ClassifyFailure_ReturnsSandboxError_WhenCodeexError()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "codeex sandbox execution environment error"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InternalError);
        classification.Reason.Should().Be("Codex sandbox error");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(30);
    }

    [Fact]
    public void ClassifyFailure_ReturnsToolError_WhenToolExecutionFailed()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "tool function call tool_use failed"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.InvalidInput);
        classification.Reason.Should().Be("Tool execution failed");
        classification.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void ClassifyFailure_ReturnsResourceExhausted_WhenExitCode137()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "container killed",
            Metadata = new Dictionary<string, string> { ["exitCode"] = "137" }
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.ResourceExhausted);
        classification.Reason.Should().Be("Container killed (OOM)");
        classification.IsRetryable.Should().BeTrue();
        classification.SuggestedBackoffSeconds.Should().Be(60);
    }

    [Theory]
    [InlineData("unauthorized", FailureClass.AuthenticationError)]
    [InlineData("invalid api key", FailureClass.AuthenticationError)]
    [InlineData("rate limit", FailureClass.RateLimitExceeded)]
    [InlineData("timeout", FailureClass.Timeout)]
    [InlineData("out of memory", FailureClass.ResourceExhausted)]
    [InlineData("invalid input", FailureClass.InvalidInput)]
    [InlineData("not found", FailureClass.NotFound)]
    [InlineData("permission denied", FailureClass.PermissionDenied)]
    [InlineData("network error", FailureClass.NetworkError)]
    [InlineData("config error", FailureClass.ConfigurationError)]
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
    public void ClassifyFailure_ReturnsUnknown_WhenNoPatternMatches()
    {
        var envelope = new HarnessResultEnvelope
        {
            Status = "failed",
            Error = "some unknown error occurred"
        };

        var classification = _adapter.ClassifyFailure(envelope);

        classification.Class.Should().Be(FailureClass.Unknown);
        classification.IsRetryable.Should().BeTrue();
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
            Artifacts = ["/workspace/output.txt", "/workspace/result.json"]
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().HaveCount(2);
        artifacts.Should().Contain(a => a.Name == "output.txt" && a.Type == "text");
        artifacts.Should().Contain(a => a.Name == "result.json" && a.Type == "json");
    }

    [Fact]
    public void MapArtifacts_IncludesPatchFile_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["patchFile"] = "/workspace/changes.patch" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "changes.patch" && a.Type == "diff");
    }

    [Fact]
    public void MapArtifacts_IncludesOutputFile_WhenPresent()
    {
        var envelope = new HarnessResultEnvelope
        {
            Metadata = new Dictionary<string, string> { ["outputFile"] = "/workspace/output.md" }
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.Should().Contain(a => a.Name == "output.md" && a.Type == "markdown");
    }

    [Fact]
    public void MapArtifacts_DeterminesCorrectTypes()
    {
        var envelope = new HarnessResultEnvelope
        {
            Artifacts =
            [
                "/workspace/file.md",
                "/workspace/file.json",
                "/workspace/file.yaml",
                "/workspace/file.yml",
                "/workspace/file.txt",
                "/workspace/file.log",
                "/workspace/file.diff",
                "/workspace/file.cs",
                "/workspace/file.js",
                "/workspace/file.py",
                "/workspace/file.go",
                "/workspace/file.rs"
            ]
        };

        var artifacts = _adapter.MapArtifacts(envelope);

        artifacts.First(a => a.Path.EndsWith(".md")).Type.Should().Be("markdown");
        artifacts.First(a => a.Path.EndsWith(".json")).Type.Should().Be("json");
        artifacts.First(a => a.Path.EndsWith(".yaml")).Type.Should().Be("yaml");
        artifacts.First(a => a.Path.EndsWith(".yml")).Type.Should().Be("yaml");
        artifacts.First(a => a.Path.EndsWith(".txt")).Type.Should().Be("text");
        artifacts.First(a => a.Path.EndsWith(".log")).Type.Should().Be("log");
        artifacts.First(a => a.Path.EndsWith(".diff")).Type.Should().Be("diff");
        artifacts.First(a => a.Path.EndsWith(".cs")).Type.Should().Be("csharp");
        artifacts.First(a => a.Path.EndsWith(".js")).Type.Should().Be("javascript");
        artifacts.First(a => a.Path.EndsWith(".py")).Type.Should().Be("python");
        artifacts.First(a => a.Path.EndsWith(".go")).Type.Should().Be("go");
        artifacts.First(a => a.Path.EndsWith(".rs")).Type.Should().Be("rust");
    }

    [Fact]
    public void ParseEnvelope_ParsesValidJsonEnvelope()
    {
        var stdout = """{"status":"succeeded","summary":"Task completed","error":""}""";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Status.Should().Be("succeeded");
        envelope.Summary.Should().Be("Task completed");
    }

    [Fact]
    public void ParseEnvelope_CreatesFallback_WhenInvalidJson()
    {
        var stdout = "plain text output";

        var envelope = _adapter.ParseEnvelope(stdout, "some stderr", 1);

        envelope.Status.Should().Be("failed");
        envelope.Summary.Should().Be("Task failed");
        envelope.Error.Should().Be("some stderr");
        envelope.Metadata["stdout"].Should().Be("plain text output");
        envelope.Metadata["exitCode"].Should().Be("1");
    }

    [Fact]
    public void ParseEnvelope_CreatesSuccessFallback_WhenExitCodeZero()
    {
        var stdout = "plain text output";

        var envelope = _adapter.ParseEnvelope(stdout, "", 0);

        envelope.Status.Should().Be("succeeded");
        envelope.Summary.Should().Be("Task completed");
    }

    private static DispatchJobRequest CreateRequest()
    {
        var request = new DispatchJobRequest
        {
            RunId = "test-run-id",
            Harness = "codex",
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
