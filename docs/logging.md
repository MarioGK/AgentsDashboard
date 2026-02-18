# Logging

## ZLogger Standard

The repository now uses `ZLogger` for all runtime logging in:
- `src/AgentsDashboard.ControlPlane`
- `src/AgentsDashboard.TaskRuntimeGateway`

## Why ZLogger

- Low-overhead structured logging with interpolation-based APIs (`ZLog*`).
- Friendly plain-text output by default for local operators.
- Better readability for high-volume orchestration events when logs include rich fields.

## Configuration

Both hosts register the provider in `Program.cs`:

- Clear default providers.
- Add ZLogger console output.
- Use plain text formatter (non-JSON).

```csharp
builder.Logging
    .ClearProviders()
    .AddZLoggerConsole(options => options.UsePlainTextFormatter());
```

## Usage Pattern

Use `ZLog*` methods with named variables for structure:

```csharp
logger.ZLogInformation($"Run {runId} dispatched to worker {workerId}");
logger.ZLogWarning($"Scale-down skipped because {queueDepth} items still active");
logger.ZLogError(ex, $"Worker bootstrap failed for {repositoryId} on image {imageTag}");
```

Template style is also valid for compatibility and still preserves structured fields:

```csharp
logger.ZLogInformation("Run {RunId} dispatched to worker {WorkerId}", runId, workerId);
logger.ZLogInformation($"Reconciliation complete: checked={checkedCount}, started={startedCount}, stopped={stoppedCount}, errors={errorCount}");
```

## Operational Defaults

- Plain text output is required for this deployment path.
- Use structured context variables directly in each message (IDs, counters, durations, reasons).
- Prefer fewer log lines with richer context fields to reduce noise in high-rate paths.
- Keep secret values out of logs; pass only IDs and derived metadata.

## Notes

- `Log*` calls are also valid at runtime if they remain in test code or third-party paths, but the hosted services should prefer `ZLog*`.
- Future updates: if JSON output is needed for centralized pipelines, swap `UsePlainTextFormatter` with `UseJsonFormatter` in `Program.cs`.
