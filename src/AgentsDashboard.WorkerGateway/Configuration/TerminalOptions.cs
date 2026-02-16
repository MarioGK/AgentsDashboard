namespace AgentsDashboard.WorkerGateway.Configuration;

public sealed class TerminalOptions
{
    public const string SectionName = "Terminal";

    public int IdleTimeoutMinutes { get; set; } = 30;
    public int ResumeGraceMinutes { get; set; } = 10;
    public int MaxConcurrentSessionsPerWorker { get; set; } = 20;
    public int MaxChunkBytes { get; set; } = 8192;
    public string DefaultImage { get; set; } = "ai-harness:latest";
}
