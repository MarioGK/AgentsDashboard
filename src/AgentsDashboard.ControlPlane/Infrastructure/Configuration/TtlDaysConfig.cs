namespace AgentsDashboard.ControlPlane.Infrastructure.Configuration;

public sealed class TtlDaysConfig
{
    public int Logs { get; set; } = 30;
    public int Runs { get; set; } = 90;
}
