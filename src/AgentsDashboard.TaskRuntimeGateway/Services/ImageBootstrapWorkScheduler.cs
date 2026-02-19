using System.Diagnostics;
using System.Threading.Channels;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class ImageBootstrapWorkScheduler : BackgroundService
{
    private readonly DockerClient _client;
    private readonly TaskRuntimeOptions _options;
    private readonly ILogger<ImageBootstrapWorkScheduler> _logger;
    private readonly SemaphoreSlim _semaphore = new(3);
    private readonly Channel<ImagePrePullPolicy> _queue = Channel.CreateUnbounded<ImagePrePullPolicy>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object _queueStateLock = new();
    private bool _warmupQueuedOrRunning;

    public ImageBootstrapWorkScheduler(
        IOptions<TaskRuntimeOptions> options,
        ILogger<ImageBootstrapWorkScheduler> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new DockerClientConfiguration().CreateClient();
    }

    public Task EnqueueImageWarmupAsync(ImagePrePullPolicy policy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_queueStateLock)
        {
            if (_warmupQueuedOrRunning)
            {
                _logger.LogDebug("Image warmup request ignored because warmup is already queued or running.");
                return Task.CompletedTask;
            }

            _warmupQueuedOrRunning = true;
        }

        if (!_queue.Writer.TryWrite(policy))
        {
            lock (_queueStateLock)
            {
                _warmupQueuedOrRunning = false;
            }

            throw new InvalidOperationException("Unable to queue image warmup request.");
        }

        _logger.LogInformation("Queued image warmup with policy {Policy}", policy);
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            ImagePrePullPolicy policy;
            try
            {
                policy = await _queue.Reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await RunWarmupAsync(policy, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image warmup background cycle failed");
            }
            finally
            {
                lock (_queueStateLock)
                {
                    _warmupQueuedOrRunning = false;
                }
            }
        }
    }

    private async Task RunWarmupAsync(ImagePrePullPolicy policy, CancellationToken cancellationToken)
    {
        var images = GetUniqueImages();

        if (images.Count == 0)
        {
            _logger.LogInformation("No harness images configured for pre-pull");
            return;
        }

        _logger.LogInformation("Starting background image warmup for {Count} harness images using policy {Policy}", images.Count, policy);

        var tasks = images.Select(image => PullImageAsync(image, policy, cancellationToken));
        await Task.WhenAll(tasks);

        _logger.LogInformation("Completed background image warmup for harness images");
    }

    private HashSet<string> GetUniqueImages()
    {
        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_options.DefaultImage))
        {
            images.Add(_options.DefaultImage);
        }

        foreach (var image in _options.HarnessImages.Values)
        {
            if (!string.IsNullOrWhiteSpace(image))
            {
                images.Add(image);
            }
        }

        return images;
    }

    private async Task PullImageAsync(string image, ImagePrePullPolicy policy, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var existsLocally = await ImageExistsLocallyAsync(image, cancellationToken);
            if (policy == ImagePrePullPolicy.MissingOnly && existsLocally)
            {
                _logger.LogInformation("Image {Image} already exists locally, skipping warmup", image);
                return;
            }

            if (await TryBuildImageAsync(image, cancellationToken))
            {
                _logger.LogInformation("Successfully built image {Image}", image);
                return;
            }

            _logger.LogInformation("Pulling image {Image} during warmup", image);

            var authConfig = GetAuthConfig(image);

            var progress = new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.Status))
                {
                    _logger.LogDebug("Warmup pull {Image}: {Status}", image, msg.Status);
                }
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
            _logger.LogError(ex, "Failed to warm image {Image}", image);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<bool> TryBuildImageAsync(string image, CancellationToken cancellationToken)
    {
        var definition = ResolveBuildDefinition(image);
        if (definition is null)
        {
            return false;
        }

        var root = ResolveWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var dockerfilePath = Path.Combine(root, definition.Value.DockerfileRelativePath);
        var contextPath = Path.Combine(root, definition.Value.ContextRelativePath);

        if (!File.Exists(dockerfilePath) || !Directory.Exists(contextPath))
        {
            return false;
        }

        _logger.LogInformation("Building image {Image} from {Dockerfile}", image, dockerfilePath);
        return await BuildImageWithCliAsync(image, dockerfilePath, contextPath, cancellationToken);
    }

    private async Task<bool> BuildImageWithCliAsync(
        string image,
        string dockerfilePath,
        string contextPath,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("build");
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(image);
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(dockerfilePath);
        info.ArgumentList.Add(contextPath);

        using var process = new Process { StartInfo = info };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _logger.LogDebug("Docker build output ({Image}): {Message}", image, args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _logger.LogDebug("Docker build error ({Image}): {Message}", image, args.Data);
            }
        };

        if (!process.Start())
        {
            _logger.LogWarning("Failed to launch docker CLI to build image {Image}", image);
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
        {
            return true;
        }

        _logger.LogWarning("docker build for image {Image} failed with exit code {ExitCode}", image, process.ExitCode);
        return false;
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
        {
            return null;
        }

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

    private static (string DockerfileRelativePath, string ContextRelativePath)? ResolveBuildDefinition(string imageReference)
    {
        var repositoryName = GetImageRepositoryName(imageReference);
        return repositoryName switch
        {
            "ai-harness" => ("deploy/harness-image/Dockerfile", "deploy/harness-image"),
            "ai-harness-base" => ("deploy/harness-images/Dockerfile.harness-base", "deploy/harness-images"),
            "harness-codex" => ("deploy/harness-images/Dockerfile.harness-codex", "deploy/harness-images"),
            "harness-opencode" => ("deploy/harness-images/Dockerfile.harness-opencode", "deploy/harness-images"),
            _ => null
        };
    }

    private static string GetImageRepositoryName(string imageReference)
    {
        var withoutDigest = imageReference.Split('@', 2)[0];
        var lastSlash = withoutDigest.LastIndexOf('/');
        var candidate = lastSlash >= 0 ? withoutDigest[(lastSlash + 1)..] : withoutDigest;

        var lastColon = candidate.LastIndexOf(':');
        return lastColon > 0 ? candidate[..lastColon].ToLowerInvariant() : candidate.ToLowerInvariant();
    }

    private static string? ResolveWorkspaceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var resolved = ResolveWorkspaceRoot(start);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? ResolveWorkspaceRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            var marker = Path.Combine(current.FullName, "deploy", "harness-image", "Dockerfile");
            if (File.Exists(marker))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
