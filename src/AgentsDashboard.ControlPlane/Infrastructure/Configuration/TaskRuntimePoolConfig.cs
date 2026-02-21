namespace AgentsDashboard.ControlPlane.Configuration;

public sealed class TaskRuntimePoolConfig
{
    public int MaxTaskRuntimes { get; set; } = 100;
    public int ParallelSlotsPerTaskRuntime { get; set; } = 1;
    public int IdleTimeoutMinutes { get; set; } = 5;
    public int StartupTimeoutSeconds { get; set; } = 60;
    public string ContainerImage { get; set; } = "ghcr.io/mariogk/ai-harness:latest";
    public string ContainerNamePrefix { get; set; } = "task-runtime";
    public string DockerNetwork { get; set; } = "agentsdashboard";
    public TaskRuntimeConnectivityMode ConnectivityMode { get; set; } = TaskRuntimeConnectivityMode.AutoDetect;
    public bool EnablePressureScaling { get; set; } = true;
    public int CpuScaleOutThresholdPercent { get; set; } = 85;
    public int MemoryScaleOutThresholdPercent { get; set; } = 85;
    public int PressureSampleWindowSeconds { get; set; } = 30;
}
