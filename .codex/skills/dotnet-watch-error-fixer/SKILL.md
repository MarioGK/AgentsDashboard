---
name: dotnet-watch-error-fixer
description: Launches dotnet watch and continuously auto-fixes log events and repeated health-check failures via non-interactive Codex sessions.
---

# dotnet-watch-error-fixer

Use this skill when you want continuous `dotnet watch` operation, deterministic restart behavior, and automated recovery actions.

## What this skill does

1. Enforces launcher singleton lock (`AUTOFIX_LOCK_FILE`) so only one launcher process runs.
2. Stops existing `dotnet watch` and releases stale listeners on startup ports before launch.
3. Starts:
   `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 DOTNET_WATCH_NONINTERACTIVE=1 DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development dotnet watch --non-interactive --project src/AgentsDashboard.ControlPlane`
4. Watches warning/error-like events from the unified log stream (`AUTOFIX_SINGLE_LOG_FILE`).
5. Classifies events into `compile/runtime/startup` taxonomy.
6. Applies compile-loop dispatch gating to avoid duplicate dispatch storms.
7. Appends matching events to:
   - `AUTOFIX_SINGLE_LOG_FILE` (default `/tmp/autofix/logs/autofix.log`)
8. Dispatches Codex using `--yolo` in non-interactive mode for actionable events.
9. Probes `HEALTH_CHECK_URL` on a random 5-10 second interval for 10 attempts.
10. On repeated failures, builds a multi-log context bundle and dispatches a focused Codex recovery attempt.
11. Restarts `dotnet watch` automatically with exponential backoff.

## Required behavior

- Non-interactive, no manual prompts.
- Long-lived run-loop until explicit stop.
- Repo-scoped actions only.

## Default command

```bash
bash .codex/skills/dotnet-watch-error-fixer/launch-and-fix.sh
```

## Environment knobs

- `REPO_ROOT`
- `WATCH_PROJECT` (default: `src/AgentsDashboard.ControlPlane`)
- `MONITOR_NORMAL_LOGS` (`true|false`, default `false`)
- `FIX_COOLDOWN_SECONDS` (default `10`)
- `AUTOFIX_SINGLE_LOG_FILE` (default: `/tmp/autofix/logs/autofix.log`)
- `WATCH_LOG_FILE` (defaults to `AUTOFIX_SINGLE_LOG_FILE`)
- `RESTART_DELAY_SECONDS` (default `3`)
- `RESTART_MAX_DELAY_SECONDS` (default `30`)
- `UNIFIED_ERROR_LOG` (defaults to `AUTOFIX_SINGLE_LOG_FILE`)
- `DOTNET_WATCH_STDOUT_LOG` (defaults to `AUTOFIX_SINGLE_LOG_FILE`)
- `DOTNET_WATCH_STDERR_LOG` (defaults to `AUTOFIX_SINGLE_LOG_FILE`)
- `AUTOFIX_DISPATCH_LOG` (defaults to `AUTOFIX_SINGLE_LOG_FILE`)
- `AUTOFIX_STATE_FILE`
- `CODEX_CLI` (default `codex`)
- `CODEX_CLI_ARGS` (optional additional args)
- `HEALTH_CHECK_ENABLED` (default `true`)
- `HEALTH_CHECK_URL` (default `https://localhost:5266/health`)
- `HEALTH_CHECK_MAX_ATTEMPTS` (default `10`)
- `HEALTH_CHECK_MIN_INTERVAL_SECONDS` (default `5`)
- `HEALTH_CHECK_MAX_INTERVAL_SECONDS` (default `10`)
- `HEALTH_CHECK_TIMEOUT_SECONDS` (default `5`)
- `HEALTH_CONTEXT_LINES` (default `120`)
- `HEALTH_FIX_COOLDOWN_SECONDS` (default `90`)
- `COMPILE_COOLDOWN_SECONDS` (default `30`)
- `STARTUP_COOLDOWN_SECONDS` (default `25`)
- `RUNTIME_COOLDOWN_SECONDS` (default `10`)
- `AUTOFIX_LOCK_DIR`
- `AUTOFIX_LOCK_FILE`
- `PRESTART_CLEANUP_DOTNET_WATCH` (`true|false`, default `true`)
- `PRESTART_CLEANUP_PORTS` (default `5266,5268`)
- `PRESTART_CLEANUP_TIMEOUT_SECONDS` (default `10`)
