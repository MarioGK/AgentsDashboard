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
}
