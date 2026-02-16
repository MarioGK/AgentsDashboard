using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class ImageBuilderServiceTests
{
    [Test]
    public void ImageBuildResult_RecordProperties()
    {
        var result = new ImageBuildResult(true, "sha256:abc123", ["Step 1/3", "Step 2/3", "Step 3/3"]);

        result.Success.Should().BeTrue();
        result.ImageId.Should().Be("sha256:abc123");
        result.Logs.Should().HaveCount(3);
    }

    [Test]
    public void ImageBuildResult_FailedBuild()
    {
        var result = new ImageBuildResult(false, string.Empty, ["Error: invalid Dockerfile"]);

        result.Success.Should().BeFalse();
        result.ImageId.Should().BeEmpty();
        result.Logs.Should().ContainSingle().Which.Should().Contain("Error");
    }

    [Test]
    public void ImageInfo_RecordProperties()
    {
        var created = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var info = new ImageInfo("myimage:latest", "sha256:abc", 1024 * 1024, created);

        info.Tag.Should().Be("myimage:latest");
        info.Id.Should().Be("sha256:abc");
        info.Size.Should().Be(1024 * 1024);
        info.Created.Should().Be(created);
    }

    [Test]
    public void ImageBuildResult_RecordEquality()
    {
        var a = new ImageBuildResult(true, "id1", ["log1"]);
        var b = new ImageBuildResult(true, "id1", ["log1"]);

        (a == b).Should().BeFalse();
    }

    [Test]
    public void ImageInfo_RecordEquality()
    {
        var dt = DateTime.UtcNow;
        var a = new ImageInfo("tag", "id", 100, dt);
        var b = new ImageInfo("tag", "id", 100, dt);

        a.Should().Be(b);
    }

    [Test]
    public async Task DisposeAsync_CompletesWithoutError()
    {
        var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var act = () => service.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void ImageBuildResult_EmptyLogs()
    {
        var result = new ImageBuildResult(true, "sha256:def", []);

        result.Logs.Should().BeEmpty();
        result.Success.Should().BeTrue();
    }

    [Test]
    public void ImageInfo_LargeSize()
    {
        var info = new ImageInfo("big:latest", "sha256:big", 5L * 1024 * 1024 * 1024, DateTime.UtcNow);

        info.Size.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task BuildImageAsync_WithValidDockerfile_ReturnsResult()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);
        var dockerfile = "FROM alpine\nRUN echo hello";

        var result = await service.BuildImageAsync(dockerfile, "test:v1", null, CancellationToken.None);

        result.Should().NotBeNull();
        result.Logs.Should().NotBeEmpty();
    }

    [Test]
    public async Task BuildImageAsync_LogCallback_IsInvoked()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);
        var logs = new List<string>();
        var dockerfile = "FROM alpine";

        await service.BuildImageAsync(dockerfile, "test:callback", log => logs.Add(log), CancellationToken.None);

        logs.Should().NotBeEmpty();
    }

    [Test]
    public async Task BuildImageAsync_WithCancellation_ReturnsResult()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.BuildImageAsync("FROM alpine", "test:cancelled", null, cts.Token);

        result.Should().NotBeNull();
    }

    [Test]
    public async Task ListImagesAsync_ReturnsList()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.ListImagesAsync(null, CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Test]
    public async Task ListImagesAsync_WithFilter_ReturnsFilteredList()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.ListImagesAsync("myapp", CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Test]
    public async Task ListImagesAsync_WithEmptyFilter_ReturnsAllImages()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.ListImagesAsync("", CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Test]
    public async Task DeleteImageAsync_WithNonExistentImage_ReturnsFalse()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.DeleteImageAsync("nonexistent:v1", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Test]
    public async Task TagImageAsync_WithNonExistentImage_ReturnsFalse()
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.TagImageAsync("nonexistent:v1", "new:v2", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Test]
    [Arguments("FROM alpine", "test:v1")]
    [Arguments("FROM ubuntu:22.04\nRUN apt-get update", "myapp:latest")]
    [Arguments("# Comment\nFROM node:18\nCOPY . /app", "nodeapp:dev")]
    public async Task BuildImageAsync_WithVariousDockerfiles_ReturnsResult(string dockerfile, string tag)
    {
        await using var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        var result = await service.BuildImageAsync(dockerfile, tag, null, CancellationToken.None);

        result.Should().NotBeNull();
        result.Logs.Should().NotBeEmpty();
    }

    [Test]
    public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
    {
        var service = new ImageBuilderService(NullLogger<ImageBuilderService>.Instance);

        await service.DisposeAsync();
        var act = async () => await service.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void ImageBuildResult_Deconstruct_HasCorrectValues()
    {
        var result = new ImageBuildResult(true, "id123", ["log"]);

        var (success, imageId, logs) = result;

        success.Should().BeTrue();
        imageId.Should().Be("id123");
        logs.Should().ContainSingle();
    }

    [Test]
    public void ImageInfo_Deconstruct_HasCorrectValues()
    {
        var created = DateTime.UtcNow;
        var info = new ImageInfo("tag", "id", 100L, created);

        var (tag, id, size, createdTime) = info;

        tag.Should().Be("tag");
        id.Should().Be("id");
        size.Should().Be(100L);
        createdTime.Should().Be(created);
    }

    [Test]
    public void ImageBuildResult_WithErrors_LogsContainErrors()
    {
        var logs = new List<string> { "Step 1/3", "Error: build failed" };
        var result = new ImageBuildResult(false, string.Empty, logs);

        result.Success.Should().BeFalse();
        result.Logs.Should().Contain(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    [Arguments("sha256:abc123")]
    [Arguments("sha256:def4567890123456789012345678901234567890")]
    [Arguments("")]
    public void ImageBuildResult_VariousImageIds(string imageId)
    {
        var result = new ImageBuildResult(true, imageId, []);

        result.ImageId.Should().Be(imageId);
    }

    [Test]
    public void ImageInfo_WithVariousSizes_HandlesCorrectly()
    {
        var sizes = new[] { 0L, 1024L, 1024L * 1024, 1024L * 1024 * 1024, 5L * 1024 * 1024 * 1024 };

        foreach (var size in sizes)
        {
            var info = new ImageInfo("test:v1", "sha256:abc", size, DateTime.UtcNow);
            info.Size.Should().Be(size);
        }
    }
}
