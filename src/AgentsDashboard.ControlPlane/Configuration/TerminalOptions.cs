namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    public int ReplayBufferEvents { get; set; } = 2000;
    public int ResumeGraceMinutes { get; set; } = 10;
}
