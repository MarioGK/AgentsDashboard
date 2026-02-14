using System.Net;
using System.Text.Json;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class WorkerHeartbeatServiceTests
{
    private readonly WorkerOptions _options;
    private readonly WorkerQueue _queue;
    private readonly Mock<ILogger<WorkerHeartbeatService>> _loggerMock;
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public WorkerHeartbeatServiceTests()
    {
        _options = new WorkerOptions
        {
            WorkerId = "test-worker",
            ControlPlaneUrl = "http://localhost:5266",
            MaxSlots = 4
        };
        _queue = new WorkerQueue(_options);
        _loggerMock = new Mock<ILogger<WorkerHeartbeatService>>();
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_SendsHeartbeatAfterInitialDelay()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.RequestCount.Should().BeGreaterThanOrEqualTo(1);
        _mockHandler.LastRequestUri.Should().Be("http://localhost:5266/api/workers/heartbeat");
    }

    [Fact]
    public async Task ExecuteAsync_IncludesWorkerIdInPayload()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.LastRequestContent.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<JsonElement>(_mockHandler.LastRequestContent!);
        payload.GetProperty("workerId").GetString().Should().Be("test-worker");
    }

    [Fact]
    public async Task ExecuteAsync_IncludesEndpointInPayload()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.LastRequestContent.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<JsonElement>(_mockHandler.LastRequestContent!);
        payload.GetProperty("endpoint").GetString().Should().StartWith("http://");
        payload.GetProperty("endpoint").GetString().Should().Contain(":5201");
    }

    [Fact]
    public async Task ExecuteAsync_IncludesActiveSlotsInPayload()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.LastRequestContent.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<JsonElement>(_mockHandler.LastRequestContent!);
        payload.GetProperty("activeSlots").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesMaxSlotsInPayload()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.LastRequestContent.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<JsonElement>(_mockHandler.LastRequestContent!);
        payload.GetProperty("maxSlots").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ExecuteAsync_TrimsTrailingSlashFromControlPlaneUrl()
    {
        _options.ControlPlaneUrl = "http://localhost:5266/";
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.LastRequestUri.Should().Be("http://localhost:5266/api/workers/heartbeat");
    }

    [Fact]
    public async Task ExecuteAsync_HandlesHttpFailureGracefully()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.RequestCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesHttpExceptionGracefully()
    {
        _mockHandler.ThrowException = true;
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);

        _mockHandler.RequestCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_StopsOnCancellation()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));

        await service.StartAsync(cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), CancellationToken.None);
        var countBeforeStop = _mockHandler.RequestCount;
        await service.StopAsync(CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);

        _mockHandler.RequestCount.Should().Be(countBeforeStop);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceCancellation_StopsGracefully()
    {
        _mockHandler.Response = new HttpResponseMessage(HttpStatusCode.OK);
        var service = new WorkerHeartbeatService(_options, _queue, _loggerMock.Object, _httpClient);
        using var cts = new CancellationTokenSource();

        var serviceTask = service.StartAsync(cts.Token);
        cts.Cancel();

        var act = async () => await serviceTask;
        await act.Should().NotThrowAsync();
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public bool ThrowException { get; set; }
        public int RequestCount { get; private set; }
        public string? LastRequestUri { get; private set; }
        public string? LastRequestContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString();

            if (request.Content is not null)
            {
                LastRequestContent = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (ThrowException)
            {
                throw new HttpRequestException("Network error");
            }

            return Response ?? new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
