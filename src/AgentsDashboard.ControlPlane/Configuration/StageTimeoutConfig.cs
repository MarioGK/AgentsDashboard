using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;


public enum TaskRuntimeConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}





public sealed class StageTimeoutConfig
{
    public int DefaultTaskStageTimeoutMinutes { get; set; } = 60;
    public int DefaultApprovalStageTimeoutHours { get; set; } = 24;
    public int DefaultParallelStageTimeoutMinutes { get; set; } = 90;
    public int MaxStageTimeoutHours { get; set; } = 48;
}
