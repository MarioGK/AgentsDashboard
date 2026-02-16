using System.Reflection;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class DockerContainerServiceTests
{
    [Test]
    public void ParseMemoryLimit_WithGigabyteSuffix_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("2g");

        result.Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithDecimalGigabytes_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("1.5g");

        result.Should().Be((long)(1.5 * 1024 * 1024 * 1024));
    }

    [Test]
    public void ParseMemoryLimit_WithUppercaseG_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("2G");

        result.Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithMegabyteSuffix_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("512m");

        result.Should().Be(512L * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithDecimalMegabytes_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("256.5m");

        result.Should().Be((long)(256.5 * 1024 * 1024));
    }

    [Test]
    public void ParseMemoryLimit_WithUppercaseM_ReturnsCorrectBytes()
    {
        var result = InvokeParseMemoryLimit("512M");

        result.Should().Be(512L * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithBytesOnly_ReturnsExactBytes()
    {
        var result = InvokeParseMemoryLimit("1073741824");

        result.Should().Be(1073741824L);
    }

    [Test]
    public void ParseMemoryLimit_WithWhitespace_TrimsAndParses()
    {
        var result = InvokeParseMemoryLimit("  2g  ");

        result.Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithInvalidFormat_ReturnsDefaultValue()
    {
        var result = InvokeParseMemoryLimit("invalid");

        result.Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithEmptyString_ReturnsDefaultValue()
    {
        var result = InvokeParseMemoryLimit("");

        result.Should().Be(2L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithZeroGigabytes_ReturnsZero()
    {
        var result = InvokeParseMemoryLimit("0g");

        result.Should().Be(0L);
    }

    [Test]
    public void ParseMemoryLimit_WithZeroMegabytes_ReturnsZero()
    {
        var result = InvokeParseMemoryLimit("0m");

        result.Should().Be(0L);
    }

    [Test]
    public void ParseMemoryLimit_WithLargeValue_HandlesCorrectly()
    {
        var result = InvokeParseMemoryLimit("16g");

        result.Should().Be(16L * 1024 * 1024 * 1024);
    }

    [Test]
    public void ParseMemoryLimit_WithSmallMegabyteValue_HandlesCorrectly()
    {
        var result = InvokeParseMemoryLimit("128m");

        result.Should().Be(128L * 1024 * 1024);
    }

    [Test]
    [Arguments("1g", 1073741824L)]
    [Arguments("2g", 2147483648L)]
    [Arguments("4g", 4294967296L)]
    [Arguments("256m", 268435456L)]
    [Arguments("512m", 536870912L)]
    [Arguments("1024m", 1073741824L)]
    public void ParseMemoryLimit_WithVariousValidInputs_ReturnsExpectedBytes(string input, long expected)
    {
        var result = InvokeParseMemoryLimit(input);

        result.Should().Be(expected);
    }

    [Test, Skip("Requires Docker runtime - Docker.DotNet version mismatch")]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        var service = new DockerContainerService(NullLogger<DockerContainerService>.Instance);

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
        };

        act.Should().NotThrow();
    }

    private static long InvokeParseMemoryLimit(string memoryLimit)
    {
        var method = typeof(DockerContainerService).GetMethod(
            "ParseMemoryLimit",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
            throw new InvalidOperationException("ParseMemoryLimit method not found");

        var result = method.Invoke(null, new object[] { memoryLimit });
        return result != null ? (long)result : 0L;
    }
}
