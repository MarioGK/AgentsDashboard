using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

internal enum OpenCodeRuntimeMode
{
    Default = 0,
    Plan = 1,
    Review = 2
}

internal sealed record OpenCodePermissionRule(
    string Permission,
    string Pattern,
    string Action);

internal sealed record OpenCodeRuntimePolicy(
    OpenCodeRuntimeMode Mode,
    string Agent,
    string? SystemPrompt,
    IReadOnlyList<OpenCodePermissionRule>? SessionPermissionRules);



internal static partial class OpenCodeRuntimePolicies
{
    private static readonly OpenCodePermissionRule[] s_mutationDeniedRules =
    [
        new("edit", "*", "deny"),
        new("bash", "*", "deny"),
    ];

    private const string PlanSystemPrompt =
        "You are in planning mode. Do not modify files or run shell commands. Provide a concrete implementation plan.";

    private const string ReviewSystemPrompt =
        "You are in review mode. Do not modify files or run shell commands. Focus on defects, risks, and actionable findings.";

    [GeneratedRegex(@"--mode\s+(?<mode>[a-zA-Z0-9\-_]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModeSwitchRegex();

    public static OpenCodeRuntimePolicy Resolve(HarnessRunRequest request)
    {
        return ResolveWithMode(request.Mode, request.Environment, request.Command);
    }

    public static OpenCodeRuntimePolicy Resolve(DispatchJobRequest request)
    {
        return ResolveWithMode(
            request.Mode.ToString(),
            request.EnvironmentVars,
            request.CustomArgs ?? string.Empty);
    }

    private static OpenCodeRuntimePolicy ResolveWithMode(
        string? mode,
        IReadOnlyDictionary<string, string>? env,
        string? command)
    {
        var runtimeMode = ResolveMode(env, mode, command);
        var explicitAgent = GetEnvValue(env, "OPENCODE_AGENT");
        var reviewAgent = GetEnvValue(env, "OPENCODE_REVIEW_AGENT");

        var agent = !string.IsNullOrWhiteSpace(explicitAgent)
            ? explicitAgent.Trim()
            : runtimeMode switch
            {
                OpenCodeRuntimeMode.Plan => "plan",
                OpenCodeRuntimeMode.Review when !string.IsNullOrWhiteSpace(reviewAgent) => reviewAgent.Trim(),
                OpenCodeRuntimeMode.Review => "plan",
                _ => "build"
            };

        var systemPrompt = runtimeMode switch
        {
            OpenCodeRuntimeMode.Plan => PlanSystemPrompt,
            OpenCodeRuntimeMode.Review => ReviewSystemPrompt,
            _ => null
        };

        var rules = runtimeMode switch
        {
            OpenCodeRuntimeMode.Plan or OpenCodeRuntimeMode.Review => s_mutationDeniedRules,
            _ => null
        };

        return new OpenCodeRuntimePolicy(runtimeMode, agent, systemPrompt, rules);
    }

    private static OpenCodeRuntimeMode ResolveMode(
        IReadOnlyDictionary<string, string>? env,
        string? mode,
        string? command)
    {
        var fromEnv = GetEnvValue(env, "OPENCODE_MODE")
            ?? GetEnvValue(env, "HARNESS_MODE")
            ?? GetEnvValue(env, "TASK_MODE")
            ?? GetEnvValue(env, "RUN_MODE");

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return ParseMode(fromEnv);
        }

        if (!string.IsNullOrWhiteSpace(mode))
        {
            return ParseMode(mode);
        }

        if (!string.IsNullOrWhiteSpace(command))
        {
            var match = ModeSwitchRegex().Match(command);
            if (match.Success)
            {
                return ParseMode(match.Groups["mode"].Value);
            }

            if (command.Contains("review", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("audit", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("read-only", StringComparison.OrdinalIgnoreCase) ||
                command.Contains("readonly", StringComparison.OrdinalIgnoreCase))
            {
                return OpenCodeRuntimeMode.Review;
            }

            if (command.Contains("plan", StringComparison.OrdinalIgnoreCase))
            {
                return OpenCodeRuntimeMode.Plan;
            }
        }

        return OpenCodeRuntimeMode.Default;
    }

    private static OpenCodeRuntimeMode ParseMode(string mode)
    {
        var normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "plan" or "planning" => OpenCodeRuntimeMode.Plan,
            "review" or "readonly" or "read-only" or "audit" => OpenCodeRuntimeMode.Review,
            "default" or "build" or "implement" or "sse" or "structured" or "auto" => OpenCodeRuntimeMode.Default,
            _ => OpenCodeRuntimeMode.Default
        };
    }

    private static string? GetEnvValue(IReadOnlyDictionary<string, string>? env, string key)
    {
        if (env is null)
        {
            return null;
        }

        return env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
