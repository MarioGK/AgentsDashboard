namespace AgentsDashboard.WorkerGateway.Services;

public interface IImageBootstrapWorkScheduler
{
    Task EnqueueImageWarmupAsync(ImagePrePullPolicy policy, CancellationToken ct);
}
