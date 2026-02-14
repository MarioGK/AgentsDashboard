using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.IntegrationTests;

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
    }
}
