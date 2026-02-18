namespace AgentsDashboard.WorkerGateway.Services;

public sealed class ImagePrePullService(
    IImageBootstrapWorkScheduler imageBootstrapWorkScheduler,
    ILogger<ImagePrePullService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await imageBootstrapWorkScheduler.EnqueueImageWarmupAsync(ImagePrePullPolicy.MissingOnly, cancellationToken);
        logger.ZLogInformation("Scheduled background harness image warmup.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
