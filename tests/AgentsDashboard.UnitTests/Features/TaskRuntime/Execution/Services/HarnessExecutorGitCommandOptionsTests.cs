using System.Reflection;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class HarnessExecutorGitCommandOptionsTests
{
    private static readonly MethodInfo ResolveGitCommandOptionsMethod = typeof(HarnessExecutor)
        .GetMethod("ResolveGitCommandOptions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find ResolveGitCommandOptions.");

    [Test]
    public async Task ResolveGitCommandOptions_WhenGitHubSshUrlWithTokenAndNoSshCredentials_UsesHttpsAndAuthHeader()
    {
        var requestEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gh_token"] = "token-value",
        };
        var runtimeEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var options = InvokeResolveGitCommandOptions("git@github.com:octo/private-repo.git", requestEnvironment, runtimeEnvironment);
        var cloneUrl = GetPropertyValue<string>(options, "CloneUrl");
        var argumentPrefix = GetPropertyValue<IReadOnlyList<string>>(options, "ArgumentPrefix");

        await Assert.That(cloneUrl).IsEqualTo("https://github.com/octo/private-repo.git");
        await Assert.That(argumentPrefix.Count).IsEqualTo(2);
        await Assert.That(argumentPrefix[0]).IsEqualTo("-c");
        await Assert.That(argumentPrefix[1].Contains("http.https://github.com/.extraheader=Authorization: Basic ", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task ResolveGitCommandOptions_WhenGitHubSshUrlWithTokenAndSshCredentials_PreservesSshCloneUrl()
    {
        var requestEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["gh_token"] = "token-value",
        };
        var runtimeEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AGENTSDASHBOARD_WORKER_SSH_AVAILABLE"] = "true",
        };

        var options = InvokeResolveGitCommandOptions("git@github.com:octo/private-repo.git", requestEnvironment, runtimeEnvironment);
        var cloneUrl = GetPropertyValue<string>(options, "CloneUrl");
        var argumentPrefix = GetPropertyValue<IReadOnlyList<string>>(options, "ArgumentPrefix");

        await Assert.That(cloneUrl).IsEqualTo("git@github.com:octo/private-repo.git");
        await Assert.That(argumentPrefix.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveGitCommandOptions_WhenNoToken_PreservesCloneUrlWithoutAuthHeader()
    {
        var requestEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var runtimeEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var options = InvokeResolveGitCommandOptions("git@github.com:octo/private-repo.git", requestEnvironment, runtimeEnvironment);
        var cloneUrl = GetPropertyValue<string>(options, "CloneUrl");
        var argumentPrefix = GetPropertyValue<IReadOnlyList<string>>(options, "ArgumentPrefix");

        await Assert.That(cloneUrl).IsEqualTo("git@github.com:octo/private-repo.git");
        await Assert.That(argumentPrefix.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResolveGitCommandOptions_AlwaysDisablesGitPrompt()
    {
        var runtimeEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SSH_AUTH_SOCK"] = "/ssh-agent.sock",
            ["GIT_SSH_COMMAND"] = "ssh -o StrictHostKeyChecking=no",
        };

        var options = InvokeResolveGitCommandOptions("https://github.com/octo/private-repo.git", null, runtimeEnvironment);
        var environmentVariables = GetPropertyValue<IReadOnlyDictionary<string, string>>(options, "EnvironmentVariables");

        var hasPromptSetting = environmentVariables.TryGetValue("GIT_TERMINAL_PROMPT", out var value)
            && string.Equals(value, "0", StringComparison.Ordinal);

        await Assert.That(hasPromptSetting).IsTrue();
        await Assert.That(environmentVariables.TryGetValue("SSH_AUTH_SOCK", out var authSock) && authSock == "/ssh-agent.sock").IsTrue();
        await Assert.That(environmentVariables.TryGetValue("GIT_SSH_COMMAND", out var gitSshCommand) && gitSshCommand == "ssh -o StrictHostKeyChecking=no").IsTrue();
    }

    private static object InvokeResolveGitCommandOptions(
        string cloneUrl,
        IReadOnlyDictionary<string, string>? environment,
        IReadOnlyDictionary<string, string> runtimeEnvironment)
    {
        var options = ResolveGitCommandOptionsMethod.Invoke(null, [cloneUrl, environment, runtimeEnvironment]);
        if (options is null)
        {
            throw new InvalidOperationException("ResolveGitCommandOptions returned null.");
        }

        return options;
    }

    private static T GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property is null)
        {
            throw new InvalidOperationException($"Property '{propertyName}' was not found on '{source.GetType().FullName}'.");
        }

        var value = property.GetValue(source);
        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidOperationException($"Property '{propertyName}' did not return expected type '{typeof(T).FullName}'.");
    }
}
