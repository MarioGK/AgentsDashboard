using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class SecretRedactorTests
{
    private static SecretRedactor CreateRedactor(List<string>? patterns = null)
    {
        var options = Options.Create(new WorkerOptions
        {
            SecretEnvPatterns = patterns ?? ["*_API_KEY", "*_TOKEN", "*_SECRET", "GH_TOKEN"],
        });
        return new SecretRedactor(options);
    }

    [Fact]
    public void Redact_MasksKnownSecretValues()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string>
        {
            ["CODEX_API_KEY"] = "sk-secret-12345",
            ["NORMAL_VAR"] = "not-a-secret",
        };

        var input = "Calling API with key sk-secret-12345 and var not-a-secret";
        var result = redactor.Redact(input, env);

        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("sk-secret-12345");
        result.Should().Contain("not-a-secret");
    }

    [Fact]
    public void Redact_IgnoresShortValues()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string> { ["GH_TOKEN"] = "abc" };

        var result = redactor.Redact("token is abc", env);

        result.Should().Contain("abc");
    }

    [Fact]
    public void Redact_EmptyInput_ReturnsEmpty()
    {
        var redactor = CreateRedactor();

        redactor.Redact("", null).Should().BeEmpty();
        redactor.Redact("", new Dictionary<string, string>()).Should().BeEmpty();
    }

    [Fact]
    public void Redact_NullEnv_ReturnsInput()
    {
        var redactor = CreateRedactor();

        var result = redactor.Redact("some text", null);

        result.Should().Be("some text");
    }

    [Fact]
    public void Redact_EmptyEnv_ReturnsInput()
    {
        var redactor = CreateRedactor();

        var result = redactor.Redact("some text", new Dictionary<string, string>());

        result.Should().Be("some text");
    }

    [Fact]
    public void Redact_MultipleSecrets_AllRedacted()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string>
        {
            ["GH_TOKEN"] = "ghp_1234567890",
            ["CODEX_API_KEY"] = "codex-key-abc",
        };

        var input = "Using ghp_1234567890 and codex-key-abc";
        var result = redactor.Redact(input, env);

        result.Should().NotContain("ghp_1234567890");
        result.Should().NotContain("codex-key-abc");
        result.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void Redact_PrefixPattern_MatchesSuffix()
    {
        var redactor = CreateRedactor(["MY_*"]);
        var env = new Dictionary<string, string> { ["MY_SECRET"] = "secret-value" };

        var result = redactor.Redact("Found secret-value here", env);

        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("secret-value");
    }

    [Fact]
    public void Redact_SuffixPattern_MatchesPrefix()
    {
        var redactor = CreateRedactor(["*_KEY"]);
        var env = new Dictionary<string, string> { ["API_KEY"] = "key-123" };

        var result = redactor.Redact("Using key-123", env);

        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("key-123");
    }

    [Fact]
    public void Redact_ExactPattern_MatchesExactly()
    {
        var redactor = CreateRedactor(["GH_TOKEN"]);
        var env = new Dictionary<string, string> { ["GH_TOKEN"] = "ghp_xxx" };

        var result = redactor.Redact("Token: ghp_xxx", env);

        result.Should().Contain("***REDACTED***");
        result.Should().NotContain("ghp_xxx");
    }

    [Fact]
    public void Redact_NonSecretVariable_NotRedacted()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string> { ["NORMAL_VAR"] = "normal-value" };

        var result = redactor.Redact("Value is normal-value", env);

        result.Should().Contain("normal-value");
    }

    [Fact]
    public void Redact_ValueWithMinimumLength_IsRedacted()
    {
        var redactor = CreateRedactor(["*_KEY"]);
        var env = new Dictionary<string, string> { ["API_KEY"] = "abcd" };

        var result = redactor.Redact("Key: abcd", env);

        result.Should().Contain("***REDACTED***");
    }

    [Fact]
    public void Redact_ValueBelowMinimumLength_NotRedacted()
    {
        var redactor = CreateRedactor(["*_KEY"]);
        var env = new Dictionary<string, string> { ["API_KEY"] = "abc" };

        var result = redactor.Redact("Key: abc", env);

        result.Should().Contain("abc");
    }

    [Fact]
    public void Redact_MultipleOccurrences_AllRedacted()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string> { ["GH_TOKEN"] = "ghp_token" };

        var result = redactor.Redact("Token ghp_token and again ghp_token", env);

        result.Should().NotContain("ghp_token");
        result.Split("***REDACTED***").Should().HaveCount(3);
    }

    [Fact]
    public void Redact_EmptyEnvValue_NotProcessed()
    {
        var redactor = CreateRedactor();
        var env = new Dictionary<string, string> { ["GH_TOKEN"] = "" };

        var result = redactor.Redact("Token: ", env);

        result.Should().Be("Token: ");
    }

    [Fact]
    public void Redact_CaseInsensitivePatternMatch()
    {
        var redactor = CreateRedactor(["*_TOKEN"]);
        var env = new Dictionary<string, string> { ["api_token"] = "secret-value" };

        var result = redactor.Redact("Value: secret-value", env);

        result.Should().Contain("***REDACTED***");
    }
}
