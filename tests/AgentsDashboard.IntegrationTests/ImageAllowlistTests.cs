using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.IntegrationTests;

public class ImageAllowlistTests
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

    private static HarnessExecutor CreateExecutor(WorkerOptions options)
    {
        var optionsWrapper = Options.Create(options);
        var redactor = new SecretRedactor(optionsWrapper);
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var factory = new HarnessAdapterFactory(optionsWrapper, redactor, serviceProvider);
        var dockerService = new DockerContainerService(NullLogger<DockerContainerService>.Instance);
        var artifactExtractor = new FakeArtifactExtractor();
        return new HarnessExecutor(optionsWrapper, factory, redactor, dockerService, artifactExtractor, NullLogger<HarnessExecutor>.Instance);
    }

    [Fact]
    public async Task Execute_RejectsNonAllowlistedImage()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/mariogk/harness-codex:*"],
            HarnessImages = new Dictionary<string, string>
            {
                ["evil"] = "evil.io/malicious:latest",
            },
        };

        var executor = CreateExecutor(options);

        var job = new AgentsDashboard.WorkerGateway.Models.QueuedJob
        {
            Request = new AgentsDashboard.Contracts.Worker.DispatchJobRequest
            {
                RunId = "test",
                Harness = "codex",
                Command = "echo hi",
            }
        };

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);

        result.Status.Should().Be("failed");
        result.Error.Should().Contain("not in the configured allowlist");
    }

    [Fact]
    public async Task Execute_AcceptsAllowlistedImage()
    {
        var options = new WorkerOptions
        {
            UseDocker = true,
            AllowedImages = ["ghcr.io/mariogk/harness-codex:*"],
            HarnessImages = new Dictionary<string, string>
            {
                ["codex"] = "ghcr.io/mariogk/harness-codex:latest",
            },
        };

        var executor = CreateExecutor(options);

        var job = new AgentsDashboard.WorkerGateway.Models.QueuedJob
        {
            Request = new AgentsDashboard.Contracts.Worker.DispatchJobRequest
            {
                RunId = "test",
                Harness = "codex",
                Command = "echo hi",
            }
        };

        var result = await executor.ExecuteAsync(job, null, CancellationToken.None);
        result.Error.Should().NotContain("not in the configured allowlist");
    }
}
