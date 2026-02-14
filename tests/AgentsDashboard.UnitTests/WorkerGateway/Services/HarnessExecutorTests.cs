using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class HarnessExecutorTests
{
    private sealed class FakeArtifactExtractor : IArtifactExtractor
    {
        public Task<List<ExtractedArtifact>> ExtractArtifactsAsync(
            string workspacePath,
            string runId,
            ArtifactPolicyConfig policy,
            CancellationToken cancellationToken)
            => Task.FromResult(new List<ExtractedArtifact>());
    }

    private static HarnessExecutor CreateExecutor(WorkerOptions? options = null)
    {
        var opts = Options.Create(options ?? new WorkerOptions());
        var redactor = new SecretRedactor(opts);
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var factory = new HarnessAdapterFactory(opts, redactor, serviceProvider);
        var dockerService = new DockerContainerService(NullLogger<DockerContainerService>.Instance);
        var artifactExtractor = new FakeArtifactExtractor();
        return new HarnessExecutor(opts, factory, redactor, dockerService, artifactExtractor, NullLogger<HarnessExecutor>.Instance);
    }

    private static QueuedJob CreateJob(string command = "echo test", string harness = "codex")
    {
        return new QueuedJob
        {
            Request = new DispatchJobRequest
            {
                RunId = "test-run-id",
                Command = command,
                Harness = harness,
            }
        };
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCommand_ReturnsFailedEnvelope()
    {
        var executor = CreateExecutor(new WorkerOptions { UseDocker = false });
        var job = CreateJob(command: "");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Summary.Should().Be("Task command is required");
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceCommand_ReturnsFailedEnvelope()
    {
        var executor = CreateExecutor(new WorkerOptions { UseDocker = false });
        var job = CreateJob(command: "   ");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Summary.Should().Be("Task command is required");
    }

    [Fact]
    public async Task ExecuteAsync_NonAllowlistedImage_ReturnsFailedEnvelope()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/trusted/*"],
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "evil.io/bad:latest",
            },
        };
        var executor = CreateExecutor(options);
        var job = CreateJob(harness: "codex");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Error.Should().Contain("not in the configured allowlist");
    }

    [Fact]
    public async Task ExecuteAsync_AllowlistedImage_PassesCheck()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/mariogk/*"],
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "ghcr.io/mariogk/harness-codex:latest",
            },
        };
        var executor = CreateExecutor(options);
        var job = CreateJob(harness: "codex");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Error.Should().NotContain("not in the configured allowlist");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyAllowlist_AcceptsAnyImage()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = [],
            DefaultImage = "any.registry/image:latest",
            HarnessImages = new Dictionary<string, string>(),
        };
        var executor = CreateExecutor(options);
        var job = CreateJob(harness: "codex");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Error.Should().NotContain("not in the configured allowlist");
    }

    [Fact]
    public async Task ExecuteAsync_WildcardAllowlist_MatchesPrefix()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/mariogk/*"],
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "ghcr.io/mariogk/harness-codex:v1",
            },
        };
        var executor = CreateExecutor(options);
        var job = CreateJob(harness: "codex");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Error.Should().NotContain("not in the configured allowlist");
    }

    [Fact]
    public async Task ExecuteAsync_ExactAllowlistMatch_AcceptsImage()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/mariogk/harness-codex:v1"],
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "ghcr.io/mariogk/harness-codex:v1",
            },
        };
        var executor = CreateExecutor(options);
        var job = CreateJob(harness: "codex");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Error.Should().NotContain("not in the configured allowlist");
    }

    [Fact]
    public async Task ExecuteAsync_CancelledJob_ReturnsFailedEnvelope()
    {
        var options = new WorkerOptions { UseDocker = false };
        var executor = CreateExecutor(options);
        var job = CreateJob(command: "sleep 100");

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync(job, null, cts.Token);

        result.Status.Should().Be("failed");
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_DirectExecution_WithValidCommand_ReturnsSucceeded()
    {
        var options = new WorkerOptions { UseDocker = false };
        var executor = CreateExecutor(options);
        var job = CreateJob(command: "echo 'test output'");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_DirectExecution_WithEnvelopeOutput_ParsesEnvelope()
    {
        var options = new WorkerOptions { UseDocker = false };
        var executor = CreateExecutor(options);
        var job = CreateJob(command: @"echo '{""status"":""succeeded"",""summary"":""All done""}'");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Status.Should().Be("succeeded");
        result.Summary.Should().Be("All done");
    }

    [Fact]
    public async Task ExecuteAsync_DirectExecution_WithNonEnvelopeOutput_CreatesFallback()
    {
        var options = new WorkerOptions { UseDocker = false };
        var executor = CreateExecutor(options);
        var job = CreateJob(command: "echo 'plain text output'");

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Should().NotBeNull();
        result.Metadata.Should().ContainKey("stdout");
    }

    [Theory]
    [InlineData("owner/repo", "test-task-123", "agent/repo/test-task")]
    [InlineData("simple-repo", "my-task", "agent/simple-repo/my-task")]
    [InlineData("github.com/owner/repo", "task-abc", "agent/repo/task-abc")]
    public void BuildExpectedBranchPrefix_ValidInputs_ReturnsCorrectFormat(string repository, string taskId, string expected)
    {
        var result = HarnessExecutor.BuildExpectedBranchPrefix(repository, taskId);
        
        result.Should().StartWith("agent/");
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("agent/myrepo/mytask/abc123", "agent/myrepo/mytask", "abc123", true, "")]
    [InlineData("Agent/MyRepo/MyTask/ABC123", "agent/MyRepo/MyTask", "ABC123", true, "")]
    [InlineData("feature/some-branch", "agent/myrepo/mytask", "abc123", false, "does not follow naming convention")]
    [InlineData("agent/repo", "agent/myrepo/mytask", "abc123", false, "at least 4 segments")]
    [InlineData("other/repo/task/abc123", "agent/myrepo/mytask", "abc123", false, "First segment must be 'agent'")]
    [InlineData("agent/repo/task/wrong-id", "agent/myrepo/mytask", "abc123", false, "does not end with run ID")]
    public void ValidateBranchName_VariousInputs_ReturnsExpectedResult(
        string branch, string expectedPrefix, string runId, bool expectedValid, string expectedErrorContains)
    {
        var result = HarnessExecutor.ValidateBranchName(branch, expectedPrefix, runId, out var error);
        
        result.Should().Be(expectedValid);
        if (!expectedValid)
        {
            error.Should().Contain(expectedErrorContains);
        }
        else
        {
            error.Should().BeEmpty();
        }
    }

    [Fact]
    public void ValidateBranchName_ExactMatch_ReturnsTrue()
    {
        var branch = "agent/my-repo/my-task/abc12345";
        var expectedPrefix = "agent/my-repo/my-task";
        var runId = "abc12345";

        var result = HarnessExecutor.ValidateBranchName(branch, expectedPrefix, runId, out var error);

        result.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void ValidateBranchName_WithoutAgentPrefix_ReturnsFalse()
    {
        var branch = "feature/my-branch";
        var expectedPrefix = "agent/my-repo/my-task";
        var runId = "abc123";

        var result = HarnessExecutor.ValidateBranchName(branch, expectedPrefix, runId, out var error);

        result.Should().BeFalse();
        error.Should().Contain("Must start with 'agent/'");
    }
}
