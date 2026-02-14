using BenchmarkDotNet.Running;

namespace AgentsDashboard.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<WorkerQueueBenchmarks>();
        BenchmarkRunner.Run<SignalRPublishBenchmarks>();
        BenchmarkRunner.Run<MongoOperationsBenchmarks>();
    }
}
