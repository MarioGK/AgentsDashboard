using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

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

    private static HarnessExecutor CreateExecutor(WorkerOptions? options = null, IDockerContainerService? dockerService = null)
    {
        var opts = Options.Create(options ?? new WorkerOptions());
        var redactor = new SecretRedactor(opts);
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var factory = new HarnessAdapterFactory(opts, redactor, serviceProvider);
        var docker = dockerService ?? CreateMockDockerService().Object;
        var artifactExtractor = new FakeArtifactExtractor();
        return new HarnessExecutor(opts, factory, redactor, docker, artifactExtractor, NullLogger<HarnessExecutor>.Instance);
    }

    private static Mock<IDockerContainerService> CreateMockDockerService()
    {
        var mock = new Mock<IDockerContainerService>();
        mock.Setup(x => x.CreateContainerAsync(
            It.IsAny<string>(), It.IsAny<IList<string>>(), It.IsAny<IDictionary<string, string>>(),
            It.IsAny<IDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<double>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("container-123");
        mock.Setup(x => x.StartAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mock.Setup(x => x.WaitForExitAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        mock.Setup(x => x.GetLogsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"status\":\"succeeded\",\"summary\":\"Test completed\"}");
        mock.Setup(x => x.GetContainerStatsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContainerMetrics?)null);
        return mock;
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
    [InlineData("owner/repo", "test-task", "agent/repo/test-tas")]
    [InlineData("simple-repo", "my-task", "agent/simple-repo/my-task")]
    public void BuildExpectedBranchPrefix_ValidInputs_ReturnsCorrectFormat(string repository, string taskId, string expected)
    {
        var result = HarnessExecutor.BuildExpectedBranchPrefix(repository, taskId);
        
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("agent/myrepo/mytask/abc123", "agent/myrepo/mytask", "abc123", true, "")]
    [InlineData("agent/repo/task/wrong-id", "agent/myrepo/mytask", "abc123", false, "does not end with run ID")]
    [InlineData("feature/some-branch", "agent/myrepo/mytask", "abc123", false, "does not follow naming convention")]
    [InlineData("agent/repo", "agent/myrepo/mytask", "abc123", false, "at least 4 segments")]
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

    [Fact]
    public void ValidateBranchName_CaseInsensitivePrefix_ReturnsTrue()
    {
        var branch = "AGENT/MyRepo/MyTask/abc12345";
        var expectedPrefix = "agent/MyRepo/MyTask";
        var runId = "abc12345";

        var result = HarnessExecutor.ValidateBranchName(branch, expectedPrefix, runId, out var error);

        result.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildExpectedBranchPrefix_TruncatesTaskId()
    {
        var result = HarnessExecutor.BuildExpectedBranchPrefix("my-repo", "very-long-task-id-12345");
        
        result.Should().Be("agent/my-repo/very-lon");
    }
}
