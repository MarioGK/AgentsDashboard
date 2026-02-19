namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed partial class DockerContainerService
{
    private sealed class ContainerStatsResponse
    {
        public CpuStats CpuStats { get; set; } = new();
        public CpuStats PreCpuStats { get; set; } = new();
        public MemoryStats MemoryStats { get; set; } = new();
        public BlkioStats BlkioStats { get; set; } = new();
        public Dictionary<string, NetworkStats>? Networks { get; set; }
    }

    private sealed class CpuStats
    {
        public CpuUsage CpuUsage { get; set; } = new();
        public long SystemCpuUsage { get; set; }
        public int? OnlineCpu { get; set; }
    }

    private sealed class CpuUsage
    {
        public long TotalUsage { get; set; }
    }

    private sealed class MemoryStats
    {
        public long Usage { get; set; }
        public long Limit { get; set; }
    }

    private sealed class BlkioStats
    {
        public List<BlockIoEntry>? IoServiceBytesRecursive { get; set; }
    }

    private sealed class BlockIoEntry
    {
        public string Op { get; set; } = string.Empty;
        public long Value { get; set; }
    }

    private sealed class NetworkStats
    {
        public long RxBytes { get; set; }
        public long TxBytes { get; set; }
    }
}
