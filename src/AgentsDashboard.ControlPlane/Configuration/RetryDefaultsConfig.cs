using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;


public enum TaskRuntimeConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}





public sealed class RetryDefaultsConfig
{
    public int MaxAttempts { get; set; } = 3;
    public int BackoffBaseSeconds { get; set; } = 10;
    public double BackoffMultiplier { get; set; } = 2.0;
}
