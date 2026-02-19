namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class ImagePrePullService(
    ImageBootstrapWorkScheduler imageBootstrapWorkScheduler,
    ILogger<ImagePrePullService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await imageBootstrapWorkScheduler.EnqueueImageWarmupAsync(ImagePrePullPolicy.MissingOnly, cancellationToken);
        logger.LogInformation("Scheduled background harness image warmup.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
