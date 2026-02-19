using System.ComponentModel.DataAnnotations;

namespace AgentsDashboard.ControlPlane.Configuration;


public enum TaskRuntimeConnectivityMode
{
    AutoDetect = 0,
    DockerDnsOnly = 1,
    HostPortOnly = 2
}





public sealed class TtlDaysConfig
{
    public int Logs { get; set; } = 30;
    public int Runs { get; set; } = 90;
}
