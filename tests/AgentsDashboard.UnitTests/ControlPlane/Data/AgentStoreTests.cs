using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.UnitTests.ControlPlane.Data;

public class AgentStoreTests
{
    [Test]
    public void Create_AgentDocument_HasDefaults()
    {
        var agent = new AgentDocument();

        agent.Id.Should().NotBeNullOrEmpty();
        agent.RepositoryId.Should().BeEmpty();
        agent.Name.Should().BeEmpty();
        agent.Harness.Should().Be("codex");
        agent.Prompt.Should().BeEmpty();
        agent.Command.Should().BeEmpty();
        agent.AutoCreatePullRequest.Should().BeFalse();
        agent.Enabled.Should().BeTrue();
    }

    [Test]
    public void Create_AgentDocument_WithCustomProperties()
    {
        var agent = new AgentDocument
        {
            Id = "agent-custom",
            RepositoryId = "repo-1",
            Name = "Test Agent",
            Description = "A test agent",
            Harness = "claude-code",
            Prompt = "Run all tests",
            Command = "dotnet test",
            AutoCreatePullRequest = true,
            Enabled = false
        };

        agent.Id.Should().Be("agent-custom");
        agent.RepositoryId.Should().Be("repo-1");
        agent.Name.Should().Be("Test Agent");
        agent.Description.Should().Be("A test agent");
        agent.Harness.Should().Be("claude-code");
        agent.Prompt.Should().Be("Run all tests");
        agent.Command.Should().Be("dotnet test");
        agent.AutoCreatePullRequest.Should().BeTrue();
        agent.Enabled.Should().BeFalse();
    }

    [Test]
    public void AgentDocument_RetryPolicy_Default()
    {
        var agent = new AgentDocument();

        agent.RetryPolicy.Should().NotBeNull();
        agent.RetryPolicy.MaxAttempts.Should().Be(1);
        agent.RetryPolicy.BackoffBaseSeconds.Should().Be(10);
        agent.RetryPolicy.BackoffMultiplier.Should().Be(2.0);
    }

    [Test]
    public void AgentDocument_Timeouts_Default()
    {
        var agent = new AgentDocument();

        agent.Timeouts.Should().NotBeNull();
        agent.Timeouts.ExecutionSeconds.Should().Be(600);
        agent.Timeouts.OverallSeconds.Should().Be(1800);
    }

    [Test]
    public void AgentDocument_SandboxProfile_Default()
    {
        var agent = new AgentDocument();

        agent.SandboxProfile.Should().NotBeNull();
        agent.SandboxProfile.CpuLimit.Should().Be(1.5);
        agent.SandboxProfile.MemoryLimit.Should().Be("2g");
        agent.SandboxProfile.NetworkDisabled.Should().BeFalse();
        agent.SandboxProfile.ReadOnlyRootFs.Should().BeFalse();
    }

    [Test]
    public void AgentDocument_ArtifactPolicy_Default()
    {
        var agent = new AgentDocument();

        agent.ArtifactPolicy.Should().NotBeNull();
        agent.ArtifactPolicy.MaxArtifacts.Should().Be(50);
        agent.ArtifactPolicy.MaxTotalSizeBytes.Should().Be(104_857_600);
    }

    [Test]
    public void AgentDocument_ArtifactPatterns_DefaultEmpty()
    {
        var agent = new AgentDocument();

        agent.ArtifactPatterns.Should().BeEmpty();
    }

    [Test]
    public void AgentDocument_InstructionFiles_DefaultEmpty()
    {
        var agent = new AgentDocument();

        agent.InstructionFiles.Should().BeEmpty();
    }

    [Test]
    public void AgentDocument_Enabled_DefaultTrue()
    {
        var agent = new AgentDocument();

        agent.Enabled.Should().BeTrue();
    }

    [Test]
    public void AgentDocument_Harness_DefaultCodex()
    {
        var agent = new AgentDocument();

        agent.Harness.Should().Be("codex");
    }

    [Test]
    public void AgentDocument_AutoCreatePullRequest_DefaultFalse()
    {
        var agent = new AgentDocument();

        agent.AutoCreatePullRequest.Should().BeFalse();
    }

    [Test]
    public void AgentDocument_Id_IsGenerated()
    {
        var agent1 = new AgentDocument();
        var agent2 = new AgentDocument();

        agent1.Id.Should().NotBeNullOrEmpty();
        agent1.Id.Should().HaveLength(32);
        agent1.Id.Should().NotBe(agent2.Id);
    }

    [Test]
    public void AgentDocument_CreatedAtUtc_IsRecentUtc()
    {
        var agent = new AgentDocument();

        agent.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void AgentDocument_UpdatedAtUtc_IsRecentUtc()
    {
        var agent = new AgentDocument();

        agent.UpdatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void AgentDocument_Description_DefaultEmpty()
    {
        var agent = new AgentDocument();

        agent.Description.Should().BeEmpty();
    }
}
