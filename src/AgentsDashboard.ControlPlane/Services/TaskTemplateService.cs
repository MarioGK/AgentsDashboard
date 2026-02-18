namespace AgentsDashboard.ControlPlane.Services;

using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;

public record TaskTemplate(
    string Id,
    string Name,
    string Description,
    string Harness,
    TaskKind Kind,
    string Prompt,
    string Command
);

public sealed class TaskTemplateService
{
    private readonly IDbContextFactory<OrchestratorDbContext> _dbContextFactory;
    private bool _seeded;

    private static readonly IReadOnlyList<TaskTemplate> BuiltInTemplates =
    [
        new TaskTemplate(
            "qa-browser-sweep",
            "QA Browser Sweep",
            "Automated browser testing with Playwright including stress testing and visual regression",
            "claude-code",
            TaskKind.OneShot,
            "Run Playwright tests with comprehensive coverage and stress testing.",
            "npx playwright test --reporter=html,json --trace=on --video=on --screenshot=on"
        ),
        new TaskTemplate(
            "unit-test-guard",
            "Unit Test Guard",
            "Automated unit test runner with auto-fix capabilities and PR creation on success",
            "codex",
            TaskKind.OneShot,
            "Run unit tests and automatically fix any failures.",
            "dotnet test --logger \"json;LogFileName=results.json\""
        ),
        new TaskTemplate(
            "dep-health-check",
            "Dependency Health Check",
            "Audit dependencies for security vulnerabilities and outdated packages",
            "opencode",
            TaskKind.Cron,
            "Audit project dependencies for security vulnerabilities and outdated packages.",
            "npm audit --json && npm outdated --json"
        ),
        new TaskTemplate(
            "regression-replay",
            "Regression Replay",
            "Replay recent failure scenarios to verify fixes and prevent regressions",
            "claude-code",
            TaskKind.OneShot,
            "Replay recent failure scenarios to verify that fixes are working correctly.",
            "cat /workspace/failures.json 2>/dev/null || echo 'No failure context'"
        )
    ];

    public static IReadOnlyList<TaskTemplate> GetTemplates() => BuiltInTemplates;

    public TaskTemplateService(IDbContextFactory<OrchestratorDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SeedBuiltInTemplatesAsync(cancellationToken);
    }

    private async Task SeedBuiltInTemplatesAsync(CancellationToken cancellationToken)
    {
        if (_seeded)
            return;

        var builtInTemplates = GetBuiltInTemplateDefinitions();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        foreach (var template in builtInTemplates)
        {
            var existing = await db.TaskTemplates.FirstOrDefaultAsync(x => x.TemplateId == template.TemplateId, cancellationToken);
            if (existing is null)
            {
                db.TaskTemplates.Add(template);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _seeded = true;
    }

    public async Task<List<TaskTemplateDocument>> ListTemplatesAsync(CancellationToken cancellationToken)
    {
        await SeedBuiltInTemplatesAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskTemplates
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskTemplateDocument?> GetTemplateByTemplateIdAsync(string templateId, CancellationToken cancellationToken)
    {
        await SeedBuiltInTemplatesAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TaskTemplates.FirstOrDefaultAsync(x => x.TemplateId == templateId, cancellationToken);
    }

    public async Task<TaskTemplateDocument?> UpdateTemplateAsync(string templateId, TaskTemplateDocument updated, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.TaskTemplates.FirstOrDefaultAsync(x => x.TemplateId == templateId, cancellationToken);
        if (existing is null)
            return null;
        if (!existing.IsEditable)
            return null;

        updated.Id = existing.Id;
        updated.TemplateId = existing.TemplateId;
        updated.IsBuiltIn = existing.IsBuiltIn;
        updated.IsEditable = true;
        updated.UpdatedAtUtc = DateTime.UtcNow;

        db.Entry(existing).CurrentValues.SetValues(updated);
        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.TaskTemplates.FirstOrDefaultAsync(x => x.TemplateId == templateId, cancellationToken);
        if (existing is null)
            return false;
        if (existing.IsBuiltIn)
            return false;

        db.TaskTemplates.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskTemplateDocument> CreateCustomTemplateAsync(TaskTemplateDocument template, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        template.TemplateId = $"custom-{Guid.NewGuid():N}"[..16];
        template.IsBuiltIn = false;
        template.IsEditable = true;
        template.CreatedAtUtc = DateTime.UtcNow;
        template.UpdatedAtUtc = DateTime.UtcNow;
        template.ArtifactPatterns ??= [];
        template.LinkedFailureRuns ??= [];
        template.Commands ??= [];

        db.TaskTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
        return template;
    }

    private static List<TaskTemplateDocument> GetBuiltInTemplateDefinitions()
    {
        return
        [
            new TaskTemplateDocument
            {
                TemplateId = "qa-browser-sweep",
                Name = "QA Browser Sweep",
                Description = "Automated browser testing with Playwright including stress testing and visual regression",
                Harness = "any",
                Prompt = """
Run the Playwright test suite with comprehensive coverage and stress testing.

Your tasks:
1. First, install dependencies:
   - Run `npm install` to ensure all packages are up to date
   - Verify Playwright browsers are installed with `npx playwright install`

2. Execute the Playwright test suite:
   - Run `npx playwright test` with full reporting enabled
   - Enable trace, video, and screenshot capture for all tests
   - Use `--reporter=html,json` for comprehensive output

3. Stress testing (if applicable):
   - Run tests with multiple workers to simulate concurrent load
   - Test with `--repeat-each=3` to catch flaky tests
   - Capture performance metrics during execution

4. Analyze and report:
   - Parse test results and identify failure patterns
   - Group failures by category (timeout, assertion, network, etc.)
   - Generate a structured JSON report containing:
     * Total tests run, passed, failed, skipped
     * Details of each failure with stack traces and error messages
     * Paths to screenshots, videos, and traces for failed tests
     * Performance metrics and timing data
     * Recommendations for fixing common failure patterns
   - Save the report to /workspace/results/report.json

5. Artifact collection:
   - Collect all screenshots from test-results/
   - Collect all videos and traces
   - Compress artifacts if they exceed size limits

If tests fail, investigate root causes and provide actionable insights for developers.
""",
                Commands =
                [
                    "npm install",
                    "npx playwright install --with-deps",
                    "npx playwright test --reporter=html,json --trace=on --video=on --screenshot=on"
                ],
                Kind = TaskKind.OneShot,
                AutoCreatePullRequest = false,
                RetryPolicy = new RetryPolicyConfig(MaxAttempts: 2, BackoffBaseSeconds: 30, BackoffMultiplier: 2.0),
                Timeouts = new TimeoutConfig(ExecutionSeconds: 1800, OverallSeconds: 3600),
                SandboxProfile = new SandboxProfileConfig(CpuLimit: 2.0, MemoryLimit: "4g", NetworkDisabled: false, ReadOnlyRootFs: false),
                ArtifactPolicy = new ArtifactPolicyConfig(MaxArtifacts: 100, MaxTotalSizeBytes: 524_288_000),
                ArtifactPatterns = ["**/test-results/**/*.{png,webm,zip,json}", "**/playwright-report/**/*"],
                IsBuiltIn = true,
                IsEditable = true
            },

            new TaskTemplateDocument
            {
                TemplateId = "unit-test-guard",
                Name = "Unit Test Guard",
                Description = "Automated unit test runner with auto-fix capabilities and PR creation on success",
                Harness = "any",
                Prompt = """
Run the unit test suite and automatically fix any failures.

Your tasks:
1. Detect the test framework and run tests:
   - For .NET: Run `dotnet test --logger "json;LogFileName=results.json"`
   - For Node.js: Run `npm test` or `yarn test`
   - For Python: Run `pytest --json-report` or `python -m pytest`
   - For Go: Run `go test -json ./...`
   - For Rust: Run `cargo test --message-format=json`

2. Parse test results:
   - Identify all failing tests
   - Extract error messages and stack traces
   - Categorize failures by type (assertion, timeout, dependency, etc.)

3. Auto-fix strategy:
   - For each failing test, analyze the root cause
   - Determine if the fix should be in:
     * Implementation code (logic errors)
     * Test code (outdated assertions, incorrect mocks)
     * Test data/fixtures
   - Apply fixes incrementally and re-run tests after each fix
   - Document all changes made

4. Verification:
   - Re-run the full test suite to verify all fixes
   - Ensure no regressions were introduced
   - Capture final test coverage if available

5. Pull request creation (if all tests pass):
   - Create a descriptive PR with:
     * Clear commit messages explaining each fix
     * Summary of what was fixed and why
     * Before/after test results
     * Coverage impact if available
   - Use conventional commit format for commit messages

Only create a PR if all tests pass. Otherwise, create findings for unresolved issues.
""",
                Commands =
                [
                    "dotnet test --logger \"json;LogFileName=results.json\" 2>&1 || npm test --json 2>&1 || pytest --json-report 2>&1 || go test -json ./... 2>&1 || cargo test --message-format=json 2>&1"
                ],
                Kind = TaskKind.OneShot,
                AutoCreatePullRequest = true,
                RetryPolicy = new RetryPolicyConfig(MaxAttempts: 3, BackoffBaseSeconds: 60, BackoffMultiplier: 2.0),
                Timeouts = new TimeoutConfig(ExecutionSeconds: 1200, OverallSeconds: 2400),
                SandboxProfile = new SandboxProfileConfig(CpuLimit: 2.0, MemoryLimit: "4g", NetworkDisabled: false, ReadOnlyRootFs: false),
                ArtifactPolicy = new ArtifactPolicyConfig(MaxArtifacts: 50, MaxTotalSizeBytes: 104_857_600),
                ArtifactPatterns = ["**/TestResults/**/*", "**/coverage/**/*", "**/*.trx", "**/results.json"],
                IsBuiltIn = true,
                IsEditable = true
            },

            new TaskTemplateDocument
            {
                TemplateId = "dependency-health-check",
                Name = "Dependency Health Check",
                Description = "Audit dependencies for security vulnerabilities and outdated packages",
                Harness = "any",
                Prompt = """
Audit project dependencies for security vulnerabilities and outdated packages.

Your tasks:
1. Detect the package manager and run security audits:
   - For Node.js: `npm audit --json` or `yarn audit --json`
   - For .NET: `dotnet list package --vulnerable --include-transitive --format json`
   - For Python: `pip-audit --format json` or `safety check --json`
   - For Go: `govulncheck ./... -json`
   - For Rust: `cargo audit`

2. Check for outdated packages:
   - For Node.js: `npm outdated --json` or `yarn outdated --json`
   - For .NET: `dotnet list package --outdated --include-transitive --format json`
   - For Python: `pip list --outdated --format json`
   - For Go: `go list -u -m -json all`
   - For Rust: `cargo outdated`

3. Analyze and categorize findings:
   - Security vulnerabilities by severity (critical, high, moderate, low)
   - Outdated packages with available updates
   - Breaking changes in major version updates
   - Deprecated packages requiring replacement

4. Generate comprehensive report:
   - Summary of total vulnerabilities by severity
   - List of vulnerable packages with:
     * Package name and current version
     * Vulnerability ID (CVE, GHSA, etc.)
     * Severity and CVSS score
     * Description of the vulnerability
     * Recommended fix version
     * Direct or transitive dependency flag
   - List of outdated packages with:
     * Current, wanted, and latest versions
     * Changelog highlights for major changes
     * Breaking change warnings
   - Prioritized action plan

5. Create findings for critical issues:
   - Generate findings for critical/high severity vulnerabilities
   - Include remediation commands
   - Link to relevant advisories

Save all reports to /workspace/reports/ for artifact collection.
""",
                Commands =
                [
                    "npm audit --json > /workspace/reports/npm-audit.json 2>&1 || true",
                    "npm outdated --json > /workspace/reports/npm-outdated.json 2>&1 || true",
                    "dotnet list package --vulnerable --include-transitive --format json > /workspace/reports/dotnet-vulnerable.json 2>&1 || true",
                    "dotnet list package --outdated --include-transitive --format json > /workspace/reports/dotnet-outdated.json 2>&1 || true"
                ],
                Kind = TaskKind.Cron,
                CronExpression = "0 6 * * 1",
                AutoCreatePullRequest = false,
                RetryPolicy = new RetryPolicyConfig(MaxAttempts: 2, BackoffBaseSeconds: 60, BackoffMultiplier: 2.0),
                Timeouts = new TimeoutConfig(ExecutionSeconds: 600, OverallSeconds: 1200),
                SandboxProfile = new SandboxProfileConfig(CpuLimit: 1.0, MemoryLimit: "2g", NetworkDisabled: false, ReadOnlyRootFs: false),
                ArtifactPolicy = new ArtifactPolicyConfig(MaxArtifacts: 20, MaxTotalSizeBytes: 52_428_800),
                ArtifactPatterns = ["/workspace/reports/**/*.json"],
                IsBuiltIn = true,
                IsEditable = true
            },

            new TaskTemplateDocument
            {
                TemplateId = "regression-replay",
                Name = "Regression Replay",
                Description = "Replay recent failure scenarios to verify fixes and prevent regressions",
                Harness = "any",
                Prompt = """
Replay recent failure scenarios to verify that fixes are working correctly.

Your tasks:
1. Load failure context:
   - Read the list of previously failed runs from linked failure data
   - Extract failure commands, error patterns, and reproduction steps
   - Load any saved failure artifacts for comparison

2. Environment preparation:
   - Ensure the workspace has all necessary dependencies
   - Restore any required test data or fixtures
   - Set up environment variables from original failure context

3. Execute replay scenarios:
   - Run each failure scenario command
   - Capture output and compare with original failure
   - Record timing differences and behavior changes

4. Analysis and comparison:
   - For each scenario determine:
     * PASS: Previously failing scenario now succeeds
     * FAIL: Scenario still fails (possibly with different error)
     * FLAKY: Results are inconsistent across runs
     * SKIPPED: Scenario cannot be replayed (missing data, etc.)
   - Compare error messages and stack traces
   - Identify any new failures or unexpected behavior

5. Generate regression report:
   - Summary statistics:
     * Total scenarios replayed
     * Scenarios now passing (regressions fixed)
     * Scenarios still failing
     * New failures detected
     * Flaky tests identified
   - Detailed results per scenario
   - Comparison with original failure output
   - Recommendations for remaining issues
   - Suggested follow-up tasks

6. Update findings:
   - Close resolved findings linked to passing scenarios
   - Update or create findings for persistent failures
   - Document any new issues discovered

Save the report to /workspace/regression-report.json
""",
                Commands =
                [
                    "echo 'Regression replay - scenarios loaded from linked failure context'",
                    "cat /workspace/failures.json 2>/dev/null || echo 'No failure context file found'"
                ],
                Kind = TaskKind.OneShot,
                AutoCreatePullRequest = false,
                RetryPolicy = new RetryPolicyConfig(MaxAttempts: 1, BackoffBaseSeconds: 30, BackoffMultiplier: 2.0),
                Timeouts = new TimeoutConfig(ExecutionSeconds: 900, OverallSeconds: 1800),
                SandboxProfile = new SandboxProfileConfig(CpuLimit: 2.0, MemoryLimit: "4g", NetworkDisabled: false, ReadOnlyRootFs: false),
                ArtifactPolicy = new ArtifactPolicyConfig(MaxArtifacts: 50, MaxTotalSizeBytes: 209_715_200),
                ArtifactPatterns = ["/workspace/**/*.json", "/workspace/**/*.log"],
                LinkedFailureRuns = [],
                IsBuiltIn = true,
                IsEditable = true
            }
        ];
    }
}
