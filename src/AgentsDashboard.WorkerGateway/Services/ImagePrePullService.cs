using Docker.DotNet;
using Docker.DotNet.Models;
using AgentsDashboard.WorkerGateway.Configuration;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class ImagePrePullService : IHostedService
{
    private readonly DockerClient _client;
    private readonly WorkerOptions _options;
    private readonly ILogger<ImagePrePullService> _logger;
    private readonly SemaphoreSlim _semaphore = new(3);

    public ImagePrePullService(
        IOptions<WorkerOptions> options,
        ILogger<ImagePrePullService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new DockerClientConfiguration().CreateClient();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var images = GetUniqueImages();

        if (images.Count == 0)
        {
            _logger.LogInformation("No harness images configured for pre-pull");
            return;
        }

        _logger.LogInformation("Starting pre-pull of {Count} harness images", images.Count);

        var tasks = images.Select(image => PullImageAsync(image, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed pre-pull of harness images");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private HashSet<string> GetUniqueImages()
    {
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_options.DefaultImage))
            images.Add(_options.DefaultImage);

        foreach (var image in _options.HarnessImages.Values)
        {
            if (!string.IsNullOrWhiteSpace(image))
                images.Add(image);
        }

        return images;
    }

    private async Task PullImageAsync(string image, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (await ImageExistsLocallyAsync(image, cancellationToken))
            {
                _logger.LogInformation("Image {Image} already exists locally, skipping pull", image);
                return;
            }

            _logger.LogInformation("Pulling image {Image}...", image);

            var authConfig = GetAuthConfig(image);

            var progress = new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                    _logger.LogDebug("Pull {Image}: {Status}", image, msg.Status);
            });

            await _client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = image },
                authConfig,
                progress,
                cancellationToken);

            _logger.LogInformation("Successfully pulled image {Image}", image);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pull image {Image}", image);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<bool> ImageExistsLocallyAsync(string image, CancellationToken cancellationToken)
    {
        try
        {
            var images = await _client.Images.ListImagesAsync(
                new ImagesListParameters(),
                cancellationToken);

            return images.Any(i => i.RepoTags?.Any(t => t.StartsWith(image, StringComparison.OrdinalIgnoreCase)) == true
                || i.RepoDigests?.Any(d => d.StartsWith(image, StringComparison.OrdinalIgnoreCase)) == true);
        }
        catch
        {
            return false;
        }
    }

    private AuthConfig? GetAuthConfig(string image)
    {
        if (!image.Contains('/'))
            return null;

        var parts = image.Split('/');
        var registry = parts[0];

        var usernameVar = $"{SanitizeEnvVar(registry)}_USERNAME";
        var passwordVar = $"{SanitizeEnvVar(registry)}_PASSWORD";
        var authVar = $"{SanitizeEnvVar(registry)}_AUTH";

        var auth = Environment.GetEnvironmentVariable(authVar);
        if (!string.IsNullOrEmpty(auth))
        {
            return new AuthConfig { ServerAddress = registry, Auth = auth };
        }

        var username = Environment.GetEnvironmentVariable(usernameVar);
        var password = Environment.GetEnvironmentVariable(passwordVar);

        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
        {
            return new AuthConfig
            {
                ServerAddress = registry,
                Username = username,
                Password = password
            };
        }

        return null;
    }

    private static string SanitizeEnvVar(string registry)
    {
        return registry.ToUpperInvariant()
            .Replace("-", "_")
            .Replace(".", "_");
    }
}
