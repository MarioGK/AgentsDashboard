using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class ArtifactExtractorTests : IDisposable
{
    private readonly Mock<ILogger<ArtifactExtractor>> _loggerMock;
    private readonly string _testWorkspacePath;
    private readonly string _testArtifactPath;
    private readonly ArtifactExtractor _extractor;

    public ArtifactExtractorTests()
    {
        _loggerMock = new Mock<ILogger<ArtifactExtractor>>();
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), $"workspace_{Guid.NewGuid()}");
        _testArtifactPath = Path.Combine(Path.GetTempPath(), $"artifacts_{Guid.NewGuid()}");

        Directory.CreateDirectory(_testWorkspacePath);
        Directory.CreateDirectory(_testArtifactPath);

        var options = Options.Create(new WorkerOptions
        {
            ArtifactStoragePath = _testArtifactPath
        });

        _extractor = new ArtifactExtractor(_loggerMock.Object, options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testWorkspacePath))
            Directory.Delete(_testWorkspacePath, recursive: true);
        if (Directory.Exists(_testArtifactPath))
            Directory.Delete(_testArtifactPath, recursive: true);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_WithNonExistentWorkspace_ReturnsEmptyList()
    {
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync("/nonexistent/path", "run-123", policy, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractArtifactsAsync_WithEmptyWorkspace_ReturnsEmptyList()
    {
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-123", policy, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractArtifactsAsync_WithMatchingFiles_ReturnsArtifacts()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "test.md"), "# Test Content");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-123", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("test.md");
        result[0].MimeType.Should().Be("text/markdown");
    }

    [Theory]
    [InlineData("test.md", "text/markdown")]
    [InlineData("test.json", "application/json")]
    [InlineData("test.yaml", "application/x-yaml")]
    [InlineData("test.yml", "application/x-yaml")]
    [InlineData("test.log", "text/plain")]
    [InlineData("test.txt", "text/plain")]
    [InlineData("test.xml", "application/xml")]
    [InlineData("test.html", "text/html")]
    [InlineData("test.png", "image/png")]
    [InlineData("test.jpg", "image/jpeg")]
    [InlineData("test.jpeg", "image/jpeg")]
    [InlineData("test.gif", "image/gif")]
    [InlineData("test.webp", "image/webp")]
    [InlineData("test.svg", "image/svg+xml")]
    [InlineData("test.mp4", "video/mp4")]
    [InlineData("test.webm", "video/webm")]
    [InlineData("test.zip", "application/zip")]
    [InlineData("test.tar", "application/x-tar")]
    [InlineData("test.gz", "application/gzip")]
    [InlineData("test.patch", "text/plain")]
    [InlineData("test.diff", "text/plain")]
    [InlineData("test.har", "application/json")]
    [InlineData("test.trace", "application/json")]
    public async Task ExtractArtifactsAsync_ReturnsCorrectMimeType(string fileName, string expectedMimeType)
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, fileName), "content");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-456", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].MimeType.Should().Be(expectedMimeType);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_RespectsMaxArtifactsLimit()
    {
        for (int i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, $"file{i}.md"), $"content {i}");
        }
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 5, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-789", policy, CancellationToken.None);

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_RespectsMaxTotalSizeLimit()
    {
        for (int i = 0; i < 10; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, $"file{i}.md"), new string('x', 1000));
        }
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 100, MaxTotalSizeBytes = 2500 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-size", policy, CancellationToken.None);

        result.Should().HaveCountLessThanOrEqualTo(3);
        result.Sum(r => r.SizeBytes).Should().BeLessThanOrEqualTo(2500);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_ComputesChecksum()
    {
        var content = "test content for checksum";
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "checksum.md"), content);
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-check", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].Checksum.Should().NotBeNullOrEmpty();
        result[0].Checksum.Should().HaveLength(64);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_CopiesFilesToDestination()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "copy.md"), "content to copy");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-copy", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        File.Exists(result[0].DestinationPath).Should().BeTrue();
        File.ReadAllText(result[0].DestinationPath).Should().Be("content to copy");
    }

    [Fact]
    public async Task ExtractArtifactsAsync_ExcludesGitDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_testWorkspacePath, ".git"));
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, ".git", "config.md"), "git config");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "include.md"), "include this");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-git", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("include.md");
    }

    [Fact]
    public async Task ExtractArtifactsAsync_ExcludesNodeModulesDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_testWorkspacePath, "node_modules"));
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "node_modules", "package.md"), "package");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "src.md"), "source");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-node", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("src.md");
    }

    [Fact]
    public async Task ExtractArtifactsAsync_HandlesNestedDirectories()
    {
        var subDir = Path.Combine(_testWorkspacePath, "sub", "dir");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.md"), "nested content");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-nested", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].SourcePath.Should().Contain("sub" + Path.DirectorySeparatorChar + "dir");
    }

    [Fact]
    public async Task ExtractArtifactsAsync_OnlyIncludesMatchingPatterns()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "included.md"), "md file");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "excluded.exe"), "exe file");
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "excluded.dll"), "dll file");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-pattern", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].FileName.Should().Be("included.md");
    }

    [Fact]
    public async Task ExtractArtifactsAsync_RecordsCorrectSize()
    {
        var content = new string('x', 500);
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "size.md"), content);
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-size-check", policy, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].SizeBytes.Should().Be(500);
    }

    [Fact]
    public async Task ExtractArtifactsAsync_CancellationRequested_Throws()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "cancel.md"), "content");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-cancel", policy, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractArtifactsAsync_DefaultMimeType_ForUnknownExtension()
    {
        await File.WriteAllTextAsync(Path.Combine(_testWorkspacePath, "test.unknownext"), "content");
        var policy = new ArtifactPolicyConfig { MaxArtifacts = 10, MaxTotalSizeBytes = 1024 * 1024 };

        var result = await _extractor.ExtractArtifactsAsync(_testWorkspacePath, "run-unknown", policy, CancellationToken.None);

        result.Should().BeEmpty();
    }
}
