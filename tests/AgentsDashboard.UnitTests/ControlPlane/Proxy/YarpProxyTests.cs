using System.Diagnostics;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace AgentsDashboard.UnitTests.ControlPlane.Proxy;

public class InMemoryYarpConfigProviderTests
{
    [Fact]
    public void GetConfig_InitiallyEmpty()
    {
        using var provider = new InMemoryYarpConfigProvider();
        var config = provider.GetConfig();

        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public void UpsertRoute_CreatesRouteAndCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-123", "/proxy/run-123/{**catch-all}", "http://localhost:8080");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Clusters.Should().HaveCount(1);

        var route = config.Routes.First();
        route.RouteId.Should().Be("run-123");
        route.ClusterId.Should().Be("cluster-run-123");
        route.Match.Path.Should().Be("/proxy/run-123/{**catch-all}");

        var cluster = config.Clusters.First();
        cluster.ClusterId.Should().Be("cluster-run-123");
        cluster.Destinations.Should().ContainKey("d1");
        cluster.Destinations["d1"].Address.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void UpsertRoute_WithTtl_SetsTtlEntry()
    {
        using var provider = new InMemoryYarpConfigProvider();
        var ttl = TimeSpan.FromMinutes(30);

        provider.UpsertRoute("run-456", "/proxy/run-456/{**catch-all}", "http://localhost:3000", ttl);

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_WithoutTtl_NoTtlEntry()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-789", "/proxy/run-789/{**catch-all}", "http://localhost:5000");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_ExistingRoute_UpdatesDestination()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-abc", "/proxy/run-abc/{**catch-all}", "http://localhost:1111");
        provider.UpsertRoute("run-abc", "/proxy/run-abc/{**catch-all}", "http://localhost:2222");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Clusters.Should().HaveCount(1);

        var cluster = config.Clusters.First();
        cluster.Destinations["d1"].Address.Should().Be("http://localhost:2222");
    }

    [Fact]
    public void UpsertRoute_ExistingRoute_UpdatesPathPattern()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-xyz", "/proxy/run-xyz/{**catch-all}", "http://localhost:1111");
        provider.UpsertRoute("run-xyz", "/proxy/run-xyz/updated/{**catch-all}", "http://localhost:1111");

        var config = provider.GetConfig();
        var route = config.Routes.First();
        route.Match.Path.Should().Be("/proxy/run-xyz/updated/{**catch-all}");
    }

    [Theory]
    [InlineData("run-1", "/proxy/run-1/{**catch-all}", "http://localhost:1001")]
    [InlineData("run-2", "/api/run-2/{**catch-all}", "http://localhost:1002")]
    [InlineData("run-3", "/proxy/run-3/{**catch-all}", "https://example.com:8443")]
    public void UpsertRoute_VariousInputs_CreatesCorrectRoute(string routeId, string path, string destination)
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute(routeId, path, destination);

        var config = provider.GetConfig();
        var route = config.Routes.First(r => r.RouteId == routeId);
        route.Match.Path.Should().Be(path);

        var cluster = config.Clusters.First(c => c.ClusterId == $"cluster-{routeId}");
        cluster.Destinations["d1"].Address.Should().Be(destination);
    }

    [Fact]
    public void UpsertRoute_MultipleRoutes_AllPresent()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-a", "/proxy/run-a/{**catch-all}", "http://localhost:3001");
        provider.UpsertRoute("run-b", "/proxy/run-b/{**catch-all}", "http://localhost:3002");
        provider.UpsertRoute("run-c", "/proxy/run-c/{**catch-all}", "http://localhost:3003");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(3);
        config.Clusters.Should().HaveCount(3);
    }

    [Fact]
    public void RemoveRoute_RemovesRouteAndCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-to-remove", "/proxy/run-to-remove/{**catch-all}", "http://localhost:4000");
        provider.RemoveRoute("run-to-remove");

        var config = provider.GetConfig();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public void RemoveRoute_NonExistent_NoException()
    {
        using var provider = new InMemoryYarpConfigProvider();

        var act = () => provider.RemoveRoute("non-existent");

        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveRoute_WithOtherRoutes_OnlyRemovesTarget()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-keep-1", "/proxy/run-keep-1/{**catch-all}", "http://localhost:5001");
        provider.UpsertRoute("run-remove", "/proxy/run-remove/{**catch-all}", "http://localhost:5002");
        provider.UpsertRoute("run-keep-2", "/proxy/run-keep-2/{**catch-all}", "http://localhost:5003");

        provider.RemoveRoute("run-remove");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(2);
        config.Clusters.Should().HaveCount(2);
        config.Routes.Select(r => r.RouteId).Should().BeEquivalentTo(["run-keep-1", "run-keep-2"]);
    }

    [Fact]
    public void RemoveRoute_WithTtl_RemovesTtlEntry()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-ttl", "/proxy/run-ttl/{**catch-all}", "http://localhost:6000", TimeSpan.FromMinutes(10));
        provider.RemoveRoute("run-ttl");

        var config = provider.GetConfig();
        config.Routes.Should().BeEmpty();
    }

    [Fact]
    public void GetConfig_ReturnsCurrentConfig()
    {
        using var provider = new InMemoryYarpConfigProvider();

        var config1 = provider.GetConfig();
        config1.Routes.Should().BeEmpty();

        provider.UpsertRoute("run-1", "/proxy/run-1/{**catch-all}", "http://localhost:7000");

        var config2 = provider.GetConfig();
        config2.Routes.Should().HaveCount(1);
    }

    [Fact]
    public void ChangeToken_SignalsOnUpsert()
    {
        using var provider = new InMemoryYarpConfigProvider();
        var config = provider.GetConfig();
        var changeToken = config.ChangeToken;
        var callbackInvoked = false;

        using var registration = changeToken.RegisterChangeCallback(_ => callbackInvoked = true, null);

        provider.UpsertRoute("run-new", "/proxy/run-new/{**catch-all}", "http://localhost:8000");

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void ChangeToken_SignalsOnRemove()
    {
        using var provider = new InMemoryYarpConfigProvider();
        provider.UpsertRoute("run-existing", "/proxy/run-existing/{**catch-all}", "http://localhost:8001");

        var config = provider.GetConfig();
        var changeToken = config.ChangeToken;
        var callbackInvoked = false;

        using var registration = changeToken.RegisterChangeCallback(_ => callbackInvoked = true, null);

        provider.RemoveRoute("run-existing");

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void ChangeToken_NewConfigAfterChange()
    {
        using var provider = new InMemoryYarpConfigProvider();
        var config1 = provider.GetConfig();

        provider.UpsertRoute("run-first", "/proxy/run-first/{**catch-all}", "http://localhost:9000");

        var config2 = provider.GetConfig();
        config2.Should().NotBeSameAs(config1);
    }

    [Fact]
    public void Dispose_DisposesCleanupTimer()
    {
        var provider = new InMemoryYarpConfigProvider();

        var act = () => provider.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_NoException()
    {
        var provider = new InMemoryYarpConfigProvider();

        provider.Dispose();

        var act = () => provider.Dispose();
        act.Should().NotThrow();
    }
}

public interface IProxyAuditRecorder
{
    Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken);
}

public class ProxyAuditRecorder : IProxyAuditRecorder
{
    private readonly OrchestratorStore _store;

    public ProxyAuditRecorder(OrchestratorStore store)
    {
        _store = store;
    }

    public Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken)
        => _store.RecordProxyRequestAsync(audit, cancellationToken);
}

public class TestableProxyAuditMiddleware
{
    private readonly IProxyAuditRecorder _recorder;
    private readonly Microsoft.Extensions.Logging.ILogger<TestableProxyAuditMiddleware> _logger;

    public TestableProxyAuditMiddleware(IProxyAuditRecorder recorder, Microsoft.Extensions.Logging.ILogger<TestableProxyAuditMiddleware> logger)
    {
        _recorder = recorder;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/proxy"))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        try
        {
            var runId = ExtractRouteValue(context, "runId") ?? string.Empty;
            var taskId = ExtractRouteValue(context, "taskId") ?? string.Empty;
            var repoId = ExtractRouteValue(context, "repoId") ?? string.Empty;
            var projectId = ExtractRouteValue(context, "projectId") ?? string.Empty;

            var auditDocument = new ProxyAuditDocument
            {
                RunId = runId,
                TaskId = taskId,
                RepoId = repoId,
                ProjectId = projectId,
                Path = context.Request.Path,
                StatusCode = context.Response.StatusCode,
                LatencyMs = stopwatch.Elapsed.TotalMilliseconds,
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _recorder.RecordProxyRequestAsync(auditDocument, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record proxy audit for path {Path}", context.Request.Path);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract proxy audit metadata from path {Path}", context.Request.Path);
        }
    }

    private static string? ExtractRouteValue(HttpContext context, string key)
    {
        if (context.Request.RouteValues.TryGetValue(key, out var value))
            return value?.ToString();

        var pathSegments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments is null || pathSegments.Length < 2)
            return null;

        if (key == "runId" && pathSegments.Length > 1)
            return pathSegments[1];

        return null;
    }
}

public class FakeProxyAuditRecorder : IProxyAuditRecorder
{
    public List<ProxyAuditDocument> RecordedAudits { get; } = [];
    public bool ShouldThrow { get; set; }
    public Exception? ThrowException { get; set; }

    public Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken)
    {
        if (ShouldThrow && ThrowException is not null)
            throw ThrowException;

        RecordedAudits.Add(audit);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        RecordedAudits.Clear();
        ShouldThrow = false;
        ThrowException = null;
    }
}

public class ProxyAuditMiddlewareTests
{
    private readonly FakeProxyAuditRecorder _recorder;
    private readonly TestableProxyAuditMiddleware _middleware;

    public ProxyAuditMiddlewareTests()
    {
        _recorder = new FakeProxyAuditRecorder();
        _middleware = new TestableProxyAuditMiddleware(_recorder, NullLogger<TestableProxyAuditMiddleware>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_NonProxyPath_CallsNextWithoutRecording()
    {
        var context = CreateHttpContext("/api/something");
        var nextCalled = false;

        await _middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        _recorder.RecordedAudits.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ProxyPath_RecordsAudit()
    {
        var context = CreateHttpContext("/proxy/run-abc123");
        context.Request.RouteValues["runId"] = "run-abc123";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].RunId.Should().Be("run-abc123");
    }

    [Fact]
    public async Task InvokeAsync_ProxyPath_RecordsLatency()
    {
        var context = CreateHttpContext("/proxy/run-latency");
        context.Request.RouteValues["runId"] = "run-latency";

        await _middleware.InvokeAsync(context, async _ =>
        {
            await Task.Delay(50);
        });

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].LatencyMs.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task InvokeAsync_ProxyPath_RecordsStatusCode()
    {
        var context = CreateHttpContext("/proxy/run-status");
        context.Request.RouteValues["runId"] = "run-status";
        context.Response.StatusCode = 201;

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task InvokeAsync_ProxyPath_RecordsPath()
    {
        var context = CreateHttpContext("/proxy/run-path/some/resource");
        context.Request.RouteValues["runId"] = "run-path";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].Path.Should().Be("/proxy/run-path/some/resource");
    }

    [Fact]
    public async Task InvokeAsync_ExtractsTaskId()
    {
        var context = CreateHttpContext("/proxy/run-task");
        context.Request.RouteValues["runId"] = "run-task";
        context.Request.RouteValues["taskId"] = "task-xyz";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].TaskId.Should().Be("task-xyz");
    }

    [Fact]
    public async Task InvokeAsync_ExtractsRepoId()
    {
        var context = CreateHttpContext("/proxy/run-repo");
        context.Request.RouteValues["runId"] = "run-repo";
        context.Request.RouteValues["repoId"] = "repo-123";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].RepoId.Should().Be("repo-123");
    }

    [Fact]
    public async Task InvokeAsync_ExtractsProjectId()
    {
        var context = CreateHttpContext("/proxy/run-project");
        context.Request.RouteValues["runId"] = "run-project";
        context.Request.RouteValues["projectId"] = "proj-456";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].ProjectId.Should().Be("proj-456");
    }

    [Fact]
    public async Task InvokeAsync_ExtractsAllIds()
    {
        var context = CreateHttpContext("/proxy/run-all");
        context.Request.RouteValues["runId"] = "run-all";
        context.Request.RouteValues["taskId"] = "task-all";
        context.Request.RouteValues["repoId"] = "repo-all";
        context.Request.RouteValues["projectId"] = "proj-all";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        var audit = _recorder.RecordedAudits[0];
        audit.RunId.Should().Be("run-all");
        audit.TaskId.Should().Be("task-all");
        audit.RepoId.Should().Be("repo-all");
        audit.ProjectId.Should().Be("proj-all");
    }

    [Fact]
    public async Task InvokeAsync_NoRouteValues_ExtractsRunIdFromPath()
    {
        var context = CreateHttpContext("/proxy/run-from-path/resource");
        context.Request.RouteValues.Clear();

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].RunId.Should().Be("run-from-path");
    }

    [Fact]
    public async Task InvokeAsync_NoRouteValues_ShortPath_EmptyRunId()
    {
        var context = CreateHttpContext("/proxy");
        context.Request.RouteValues.Clear();

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].RunId.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/proxy/run-1")]
    [InlineData("/proxy/run-2/resource")]
    [InlineData("/proxy/run-3/api/endpoint")]
    public async Task InvokeAsync_VariousProxyPaths_RecordsAudit(string path)
    {
        _recorder.Reset();
        var context = CreateHttpContext(path);

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("/api/runs")]
    [InlineData("/runs")]
    [InlineData("/health")]
    [InlineData("/")]
    public async Task InvokeAsync_NonProxyPaths_SkipsAudit(string path)
    {
        _recorder.Reset();
        var context = CreateHttpContext(path);

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        _recorder.RecordedAudits.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_StoreThrows_StillCompletes()
    {
        _recorder.ShouldThrow = true;
        _recorder.ThrowException = new Exception("Store error");

        var context = CreateHttpContext("/proxy/run-error");
        context.Request.RouteValues["runId"] = "run-error";

        var act = async () => await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvokeAsync_RecordsTimestamp()
    {
        var before = DateTime.UtcNow;
        var context = CreateHttpContext("/proxy/run-timestamp");
        context.Request.RouteValues["runId"] = "run-timestamp";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        await Task.Delay(100);
        var after = DateTime.UtcNow;

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].TimestampUtc.Should().BeOnOrAfter(before);
        _recorder.RecordedAudits[0].TimestampUtc.Should().BeOnOrBefore(after);
    }

    [Fact]
    public async Task InvokeAsync_ErrorStatusCode_RecordsCorrectStatus()
    {
        var context = CreateHttpContext("/proxy/run-error-status");
        context.Request.RouteValues["runId"] = "run-error-status";
        context.Response.StatusCode = 500;

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_StillAttemptsAudit()
    {
        var context = CreateHttpContext("/proxy/run-throw");
        context.Request.RouteValues["runId"] = "run-throw";

        var act = async () => await _middleware.InvokeAsync(context, _ => throw new InvalidOperationException("Next error"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact(Skip = "DestinationState is sealed and cannot be mocked")]
    public async Task InvokeAsync_ExtractsUpstreamTarget_WhenProxyFeaturePresent()
    {
        var context = CreateHttpContextWithProxyFeature("/proxy/run-upstream", "destination-123");
        context.Request.RouteValues["runId"] = "run-upstream";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].UpstreamTarget.Should().Be("destination-123");
    }

    [Fact]
    public async Task InvokeAsync_UpstreamTargetEmpty_WhenNoProxyFeature()
    {
        var context = CreateHttpContext("/proxy/run-no-proxy");
        context.Request.RouteValues["runId"] = "run-no-proxy";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);

        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].UpstreamTarget.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentRequests_AllRecordedCorrectly()
    {
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var context = CreateHttpContext($"/proxy/run-concurrent-{i}");
            context.Request.RouteValues["runId"] = $"run-concurrent-{i}";
            await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        });

        await Task.WhenAll(tasks);
        await Task.Delay(200);

        _recorder.RecordedAudits.Should().HaveCount(10);
    }

    [Fact]
    public async Task InvokeAsync_RecordsMethod_GET()
    {
        var context = CreateHttpContext("/proxy/run-method");
        context.Request.Method = "GET";
        context.Request.RouteValues["runId"] = "run-method";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        await Task.Delay(100);

        _recorder.RecordedAudits[0].Path.Should().Be("/proxy/run-method");
    }

    [Fact]
    public async Task InvokeAsync_RecordsMethod_POST()
    {
        var context = CreateHttpContext("/proxy/run-post");
        context.Request.Method = "POST";
        context.Request.RouteValues["runId"] = "run-post";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
    }

    [Fact]
    public async Task InvokeAsync_LatencyIsPositive()
    {
        var context = CreateHttpContext("/proxy/run-lat-positive");
        context.Request.RouteValues["runId"] = "run-lat-positive";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        await Task.Delay(100);

        _recorder.RecordedAudits[0].LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task InvokeAsync_HandlesQueryParameters()
    {
        var context = CreateHttpContext("/proxy/run-query?param1=value1&param2=value2");
        context.Request.RouteValues["runId"] = "run-query";

        await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
        await Task.Delay(100);

        _recorder.RecordedAudits.Should().HaveCount(1);
        _recorder.RecordedAudits[0].Path.Should().Contain("param1");
    }

    [Fact]
    public async Task InvokeAsync_AllowedStatusCodes()
    {
        var statusCodes = new[] { 200, 201, 204, 301, 302, 400, 401, 403, 404, 500, 502, 503 };

        foreach (var statusCode in statusCodes)
        {
            _recorder.Reset();
            var context = CreateHttpContext($"/proxy/run-status-{statusCode}");
            context.Request.RouteValues["runId"] = $"run-status-{statusCode}";
            context.Response.StatusCode = statusCode;

            await _middleware.InvokeAsync(context, _ => Task.CompletedTask);
            await Task.Delay(50);

            _recorder.RecordedAudits.Should().HaveCount(1);
            _recorder.RecordedAudits[0].StatusCode.Should().Be(statusCode);
        }
    }

    private static DefaultHttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.StatusCode = 200;
        return context;
    }

    private static DefaultHttpContext CreateHttpContextWithProxyFeature(string path, string destinationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        context.Response.StatusCode = 200;

        var mockDestination = new Mock<Yarp.ReverseProxy.Model.DestinationState>(destinationId, "http://localhost", "");
        var proxyFeature = new Mock<Yarp.ReverseProxy.Model.IReverseProxyFeature>();
        proxyFeature.Setup(x => x.ProxiedDestination).Returns(mockDestination.Object);

        context.Features.Set(proxyFeature.Object);

        return context;
    }
}

public class RouteOwnershipTests
{
    [Fact]
    public void UpsertRoute_ClusterIdMatchesRouteId()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("my-route-id", "/proxy/my-route-id/{**catch-all}", "http://localhost:1234");

        var config = provider.GetConfig();
        var route = config.Routes.First();
        var cluster = config.Clusters.First();

        route.RouteId.Should().Be("my-route-id");
        cluster.ClusterId.Should().Be("cluster-my-route-id");
        route.ClusterId.Should().Be(cluster.ClusterId);
    }

    [Fact]
    public void UpsertRoute_VerifiesClusterOwnershipConsistency()
    {
        using var provider = new InMemoryYarpConfigProvider();

        var routeId = "ownership-verify-test";
        provider.UpsertRoute(routeId, $"/proxy/{routeId}/{{**catch-all}}", "http://localhost:2345");

        var config = provider.GetConfig();
        var route = config.Routes.FirstOrDefault(r => r.RouteId == routeId);
        var cluster = config.Clusters.FirstOrDefault(c => c.ClusterId == $"cluster-{routeId}");

        route.Should().NotBeNull();
        cluster.Should().NotBeNull();
        route!.ClusterId.Should().Be(cluster!.ClusterId, "route must reference its owning cluster");
    }

    [Fact]
    public void UpsertRoute_ClusterHasExactlyOneDestination()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("single-dest", "/proxy/single-dest/{**catch-all}", "http://localhost:3456");

        var config = provider.GetConfig();
        var cluster = config.Clusters.First();
        cluster.Destinations.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveRoute_RemovesAssociatedCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("orphan-test", "/proxy/orphan-test/{**catch-all}", "http://localhost:4567");
        var clusterId = provider.GetConfig().Clusters.First().ClusterId;

        provider.RemoveRoute("orphan-test");

        var config = provider.GetConfig();
        config.Clusters.Should().NotContain(c => c.ClusterId == clusterId, "removing route should also remove its cluster to prevent orphans");
    }

    [Fact]
    public void UpsertRoute_NoCrossOwnershipBetweenRoutes()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("route-x", "/proxy/route-x/{**catch-all}", "http://localhost:5001");
        provider.UpsertRoute("route-y", "/proxy/route-y/{**catch-all}", "http://localhost:5002");

        var config = provider.GetConfig();
        var routeX = config.Routes.First(r => r.RouteId == "route-x");
        var routeY = config.Routes.First(r => r.RouteId == "route-y");

        routeX.ClusterId.Should().NotBe(routeY.ClusterId, "routes must not share clusters");
    }

    [Fact]
    public void RemoveRoute_OneRouteDoesNotAffectOthersOwnership()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("keep-a", "/proxy/keep-a/{**catch-all}", "http://localhost:6001");
        provider.UpsertRoute("remove-b", "/proxy/remove-b/{**catch-all}", "http://localhost:6002");
        provider.UpsertRoute("keep-c", "/proxy/keep-c/{**catch-all}", "http://localhost:6003");

        var configBefore = provider.GetConfig();
        var keepABefore = configBefore.Routes.First(r => r.RouteId == "keep-a");
        var keepCBefore = configBefore.Routes.First(r => r.RouteId == "keep-c");

        provider.RemoveRoute("remove-b");

        var configAfter = provider.GetConfig();
        var keepAAfter = configAfter.Routes.First(r => r.RouteId == "keep-a");
        var keepCAfter = configAfter.Routes.First(r => r.RouteId == "keep-c");

        keepAAfter.ClusterId.Should().Be(keepABefore.ClusterId, "remaining routes should keep their clusters");
        keepCAfter.ClusterId.Should().Be(keepCBefore.ClusterId, "remaining routes should keep their clusters");
    }

    [Fact]
    public void UpsertRoute_MultipleRoutes_EachHasOwnCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("route-a", "/proxy/route-a/{**catch-all}", "http://localhost:2001");
        provider.UpsertRoute("route-b", "/proxy/route-b/{**catch-all}", "http://localhost:2002");

        var config = provider.GetConfig();
        var routes = config.Routes.ToList();
        var clusters = config.Clusters.ToList();

        routes.Should().HaveCount(2);
        clusters.Should().HaveCount(2);

        foreach (var route in routes)
        {
            var expectedClusterId = $"cluster-{route.RouteId}";
            route.ClusterId.Should().Be(expectedClusterId);
            clusters.Should().Contain(c => c.ClusterId == expectedClusterId);
        }
    }

    [Fact]
    public void UpsertRoute_RouteToCluster_OneToOneMapping()
    {
        using var provider = new InMemoryYarpConfigProvider();

        var routeIds = new[] { "alpha", "beta", "gamma" };
        foreach (var routeId in routeIds)
        {
            provider.UpsertRoute(routeId, $"/proxy/{routeId}/{{**catch-all}}", $"http://localhost:3{routeId.GetHashCode() % 1000}");
        }

        var config = provider.GetConfig();

        foreach (var routeId in routeIds)
        {
            var route = config.Routes.FirstOrDefault(r => r.RouteId == routeId);
            route.Should().NotBeNull();

            var cluster = config.Clusters.FirstOrDefault(c => c.ClusterId == $"cluster-{routeId}");
            cluster.Should().NotBeNull();

            route!.ClusterId.Should().Be(cluster!.ClusterId);
        }
    }

    [Fact]
    public void UpsertRoute_DestinationKey_IsD1()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("dest-test", "/proxy/dest-test/{**catch-all}", "http://localhost:9999");

        var config = provider.GetConfig();
        var cluster = config.Clusters.First();

        cluster.Destinations.Should().ContainKey("d1");
        cluster.Destinations.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_UpdatePreservesOwnership()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("ownership-test", "/proxy/ownership-test/{**catch-all}", "http://localhost:4000");
        provider.UpsertRoute("ownership-test", "/proxy/ownership-test/v2/{**catch-all}", "http://localhost:5000");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Clusters.Should().HaveCount(1);

        var route = config.Routes.First();
        var cluster = config.Clusters.First();

        route.RouteId.Should().Be("ownership-test");
        cluster.ClusterId.Should().Be("cluster-ownership-test");
        route.ClusterId.Should().Be(cluster.ClusterId);
    }

    [Fact]
    public void UpsertRoute_SpecialCharactersInRouteId_CreatesCorrectCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-abc-123-xyz", "/proxy/run-abc-123-xyz/{**catch-all}", "http://localhost:6000");

        var config = provider.GetConfig();
        var route = config.Routes.First();
        var cluster = config.Clusters.First();

        cluster.ClusterId.Should().Be("cluster-run-abc-123-xyz");
        route.ClusterId.Should().Be("cluster-run-abc-123-xyz");
    }

    [Fact]
    public void RemoveRoute_CleansUpCluster()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("cleanup-test", "/proxy/cleanup-test/{**catch-all}", "http://localhost:7000");
        provider.RemoveRoute("cleanup-test");

        var config = provider.GetConfig();
        config.Routes.Should().BeEmpty();
        config.Clusters.Should().BeEmpty();
    }

    [Fact]
    public void UpsertRoute_ThenRemove_OtherRoutesUnaffected()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("keep-me", "/proxy/keep-me/{**catch-all}", "http://localhost:8001");
        provider.UpsertRoute("remove-me", "/proxy/remove-me/{**catch-all}", "http://localhost:8002");
        provider.RemoveRoute("remove-me");

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);
        config.Clusters.Should().HaveCount(1);

        var route = config.Routes.First();
        route.RouteId.Should().Be("keep-me");
        route.ClusterId.Should().Be("cluster-keep-me");
    }
}

public class TtlCleanupTests
{
    [Fact]
    public void UpsertRoute_WithExpiredTtl_CanBeCleanedUp()
    {
        using var provider = new InMemoryYarpConfigProvider();
        provider.UpsertRoute("run-expired", "/proxy/run-expired/{**catch-all}", "http://localhost:5000", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(50);

        provider.GetConfig().Routes.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_WithoutTtl_IsNotCleanedUp()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-no-ttl", "/proxy/run-no-ttl/{**catch-all}", "http://localhost:5001");

        Thread.Sleep(100);

        provider.GetConfig().Routes.Should().HaveCount(1);
    }

    [Fact]
    public void MultipleRoutes_WithMixedTtls_OnlyExpiredOnesRemoved()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-short", "/proxy/run-short/{**catch-all}", "http://localhost:6001", TimeSpan.FromMilliseconds(1));
        provider.UpsertRoute("run-long", "/proxy/run-long/{**catch-all}", "http://localhost:6002", TimeSpan.FromHours(1));
        provider.UpsertRoute("run-no-ttl", "/proxy/run-no-ttl/{**catch-all}", "http://localhost:6003");

        provider.GetConfig().Routes.Should().HaveCount(3);
    }

    [Fact]
    public void UpsertRoute_TtlUpdated_ResetsExpiration()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-reset", "/proxy/run-reset/{**catch-all}", "http://localhost:7001", TimeSpan.FromMilliseconds(1));

        Thread.Sleep(50);

        provider.UpsertRoute("run-reset", "/proxy/run-reset/{**catch-all}", "http://localhost:7002", TimeSpan.FromHours(1));

        provider.GetConfig().Routes.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_RemovingTtl_PreventsCleanup()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-convert", "/proxy/run-convert/{**catch-all}", "http://localhost:8001", TimeSpan.FromMilliseconds(1));

        provider.UpsertRoute("run-convert", "/proxy/run-convert/{**catch-all}", "http://localhost:8002");

        Thread.Sleep(100);

        provider.GetConfig().Routes.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveRoute_WithTtl_CleansUpTtlEntry()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-remove-ttl", "/proxy/run-remove-ttl/{**catch-all}", "http://localhost:9001", TimeSpan.FromMinutes(30));
        provider.RemoveRoute("run-remove-ttl");

        provider.GetConfig().Routes.Should().BeEmpty();
        provider.GetConfig().Clusters.Should().BeEmpty();
    }

    [Fact]
    public void UpsertRoute_ZeroTtl_StillSet()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-zero-ttl", "/proxy/run-zero-ttl/{**catch-all}", "http://localhost:10001", TimeSpan.Zero);

        provider.GetConfig().Routes.Should().HaveCount(1);
    }

    [Fact]
    public void UpsertRoute_NegativeTtl_StillSet()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("run-neg-ttl", "/proxy/run-neg-ttl/{**catch-all}", "http://localhost:10002", TimeSpan.FromMinutes(-5));

        provider.GetConfig().Routes.Should().HaveCount(1);
    }
}

public class YarpConfigIntegrationTests
{
    [Fact]
    public void FullLifecycle_CreateUpdateRemove()
    {
        using var provider = new InMemoryYarpConfigProvider();

        provider.UpsertRoute("lifecycle-1", "/proxy/lifecycle-1/{**catch-all}", "http://localhost:9001");
        provider.GetConfig().Routes.Should().HaveCount(1);

        provider.UpsertRoute("lifecycle-1", "/proxy/lifecycle-1/updated/{**catch-all}", "http://localhost:9002");
        provider.GetConfig().Routes.Should().HaveCount(1);
        provider.GetConfig().Routes.First().Match.Path.Should().Contain("updated");

        provider.UpsertRoute("lifecycle-2", "/proxy/lifecycle-2/{**catch-all}", "http://localhost:9003");
        provider.GetConfig().Routes.Should().HaveCount(2);

        provider.RemoveRoute("lifecycle-1");
        provider.GetConfig().Routes.Should().HaveCount(1);
        provider.GetConfig().Routes.First().RouteId.Should().Be("lifecycle-2");

        provider.RemoveRoute("lifecycle-2");
        provider.GetConfig().Routes.Should().BeEmpty();
    }

    [Fact]
    public void ConfigChangeTokens_FireOnEachChange()
    {
        using var provider = new InMemoryYarpConfigProvider();
        var changeCount = 0;

        for (int i = 0; i < 5; i++)
        {
            var config = provider.GetConfig();
            using var registration = config.ChangeToken.RegisterChangeCallback(_ => changeCount++, null);
            provider.UpsertRoute($"route-{i}", $"/proxy/route-{i}/{{**catch-all}}", $"http://localhost:{10000 + i}");
        }

        changeCount.Should().Be(5);
    }

    [Fact]
    public void RapidUpdates_LastWriteWins()
    {
        using var provider = new InMemoryYarpConfigProvider();

        for (int i = 0; i < 100; i++)
        {
            provider.UpsertRoute("rapid", "/proxy/rapid/{**catch-all}", $"http://localhost:{11000 + i}");
        }

        var config = provider.GetConfig();
        config.Routes.Should().HaveCount(1);

        var cluster = config.Clusters.First();
        cluster.Destinations["d1"].Address.Should().Be("http://localhost:11099");
    }

    [Fact]
    public void MultipleRoutes_RemovalOrder_DoesNotAffectOthers()
    {
        using var provider = new InMemoryYarpConfigProvider();

        var routeIds = Enumerable.Range(1, 10).Select(i => $"route-{i}").ToList();
        foreach (var routeId in routeIds)
        {
            provider.UpsertRoute(routeId, $"/proxy/{routeId}/{{**catch-all}}", "http://localhost:12000");
        }

        provider.GetConfig().Routes.Should().HaveCount(10);

        foreach (var routeId in routeIds.Take(5))
        {
            provider.RemoveRoute(routeId);
        }

        provider.GetConfig().Routes.Should().HaveCount(5);
        var remainingRoutes = provider.GetConfig().Routes.Select(r => r.RouteId).ToList();
        remainingRoutes.Should().BeEquivalentTo(routeIds.Skip(5));
    }
}
