using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class StageTimeoutConfigTests
{
    [Test]
    public void StageTimeoutConfig_DefaultValues()
    {
        var config = new StageTimeoutConfig();

        config.DefaultTaskStageTimeoutMinutes.Should().Be(60);
        config.DefaultApprovalStageTimeoutHours.Should().Be(24);
        config.DefaultParallelStageTimeoutMinutes.Should().Be(90);
        config.MaxStageTimeoutHours.Should().Be(48);
    }

    [Test]
    public void WorkflowStageConfig_TimeoutMinutes_CanBeSet()
    {
        var stage = new WorkflowStageConfig
        {
            Id = "stage-1",
            Name = "Test Stage",
            Type = WorkflowStageType.Task,
            TimeoutMinutes = 120
        };

        stage.TimeoutMinutes.Should().Be(120);
    }

    [Test]
    public void WorkflowStageConfig_TimeoutMinutes_DefaultsToNull()
    {
        var stage = new WorkflowStageConfig();

        stage.TimeoutMinutes.Should().BeNull();
    }

    [Test]
    [Arguments(WorkflowStageType.Task, 60)]
    [Arguments(WorkflowStageType.Approval, 1440)]
    [Arguments(WorkflowStageType.Parallel, 90)]
    [Arguments(WorkflowStageType.Delay, 60)]
    public void GetStageTimeout_ReturnsCorrectDefault(WorkflowStageType type, int expectedMinutes)
    {
        var config = new StageTimeoutConfig();
        var stage = new WorkflowStageConfig { Type = type };
        var timeout = TimeSpan.FromMinutes(expectedMinutes);

        var expectedTimeout = type switch
        {
            WorkflowStageType.Task => TimeSpan.FromMinutes(config.DefaultTaskStageTimeoutMinutes),
            WorkflowStageType.Approval => TimeSpan.FromHours(config.DefaultApprovalStageTimeoutHours),
            WorkflowStageType.Parallel => TimeSpan.FromMinutes(config.DefaultParallelStageTimeoutMinutes),
            _ => TimeSpan.FromMinutes(60)
        };

        expectedTimeout.Should().Be(timeout);
    }

    [Test]
    public void StageTimeout_UsesCustomValueWhenSet()
    {
        var stage = new WorkflowStageConfig
        {
            Type = WorkflowStageType.Task,
            TimeoutMinutes = 30
        };

        stage.TimeoutMinutes.Should().Be(30);
    }

    [Test]
    public void StageTimeout_CappedAtMaxStageTimeout()
    {
        var config = new StageTimeoutConfig
        {
            MaxStageTimeoutHours = 48
        };
        var customTimeout = TimeSpan.FromHours(72);
        var maxTimeout = TimeSpan.FromHours(config.MaxStageTimeoutHours);

        var cappedTimeout = customTimeout > maxTimeout ? maxTimeout : customTimeout;

        cappedTimeout.Should().Be(TimeSpan.FromHours(48));
    }

    [Test]
    public void DeadRunDetectionConfig_DefaultValues()
    {
        var config = new DeadRunDetectionConfig();

        config.CheckIntervalSeconds.Should().Be(60);
        config.StaleRunThresholdMinutes.Should().Be(30);
        config.ZombieRunThresholdMinutes.Should().Be(120);
        config.MaxRunAgeHours.Should().Be(24);
        config.EnableAutoTermination.Should().BeTrue();
        config.ForceKillOnTimeout.Should().BeTrue();
    }

    [Test]
    public void DeadRunDetectionConfig_CanBeDisabled()
    {
        var config = new DeadRunDetectionConfig
        {
            EnableAutoTermination = false
        };

        config.EnableAutoTermination.Should().BeFalse();
    }

    [Test]
    public void DeadRunDetectionConfig_ThresholdsCanBeConfigured()
    {
        var config = new DeadRunDetectionConfig
        {
            StaleRunThresholdMinutes = 15,
            ZombieRunThresholdMinutes = 60,
            MaxRunAgeHours = 12
        };

        config.StaleRunThresholdMinutes.Should().Be(15);
        config.ZombieRunThresholdMinutes.Should().Be(60);
        config.MaxRunAgeHours.Should().Be(12);
    }
}
