using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AgentsDashboard.WorkerGateway.Configuration;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerHeartbeatService(
    WorkerOptions options,
    WorkerQueue queue,
    ILogger<WorkerHeartbeatService> logger,
    HttpClient httpClient) : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send heartbeat to control plane");
            }

            await Task.Delay(HeartbeatInterval, stoppingToken);
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        var payload = new HeartbeatPayload
        {
            WorkerId = options.WorkerId,
            Endpoint = $"http://{Environment.MachineName}:5201",
            ActiveSlots = queue.ActiveSlots,
            MaxSlots = queue.MaxSlots
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{options.ControlPlaneUrl.TrimEnd('/')}/api/workers/heartbeat",
            payload,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            logger.LogDebug("Heartbeat sent successfully: Worker={WorkerId}, Active={Active}/{Max}",
                options.WorkerId, queue.ActiveSlots, queue.MaxSlots);
        }
        else
        {
            logger.LogWarning("Heartbeat failed with status {StatusCode}", response.StatusCode);
        }
    }

    private sealed class HeartbeatPayload
    {
        [JsonPropertyName("workerId")]
        public required string WorkerId { get; set; }

        [JsonPropertyName("endpoint")]
        public required string Endpoint { get; set; }

        [JsonPropertyName("activeSlots")]
        public int ActiveSlots { get; set; }

        [JsonPropertyName("maxSlots")]
        public int MaxSlots { get; set; }
    }
}
