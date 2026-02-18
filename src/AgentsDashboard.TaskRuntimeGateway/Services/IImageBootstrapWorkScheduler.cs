namespace AgentsDashboard.TaskRuntimeGateway.Services;

public interface IImageBootstrapWorkScheduler
{
    Task EnqueueImageWarmupAsync(ImagePrePullPolicy policy, CancellationToken ct);
}
