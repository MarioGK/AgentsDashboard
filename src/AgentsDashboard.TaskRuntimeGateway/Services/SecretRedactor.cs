using AgentsDashboard.TaskRuntimeGateway.Configuration;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class SecretRedactor(IOptions<TaskRuntimeOptions> options)
{
    private const string RedactedPlaceholder = "***REDACTED***";

    public string Redact(string input, IDictionary<string, string>? envVars)
    {
        if (string.IsNullOrEmpty(input) || envVars is null || envVars.Count == 0)
            return input;

        var result = input;
        var patterns = options.Value.SecretEnvPatterns;

        foreach (var (key, value) in envVars)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 4)
                continue;

            if (!IsSecretKey(key, patterns))
                continue;

            result = result.Replace(value, RedactedPlaceholder, StringComparison.Ordinal);
        }

        return result;
    }

    private static bool IsSecretKey(string key, List<string> patterns)
    {
        foreach (var pattern in patterns)
        {
            if (pattern.StartsWith('*') && key.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                return true;
            if (pattern.EndsWith('*') && key.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(key, pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
