---
name: dotnet-watch-error-fixer
description: Launches dotnet watch and continuously auto-fixes log events and repeated health-check failures via non-interactive Codex sessions.
---

# dotnet-watch-error-fixer

Use this skill when you want continuous `dotnet watch` operation and automated recovery actions.

## What this skill does

1. Cleans `data/logs` and dispatch logs before startup.
2. Stops existing `dotnet watch` before startup.
3. Starts:
   `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development dotnet watch --non-interactive --project src/AgentsDashboard.ControlPlane`
4. Watches warning/error-like events from `data/errors.log`, `dotnet-watch.stdout.log`, and `dotnet-watch.stderr.log`.
5. Appends matching events to:
   - `data/logs/autofix-unified-errors.log`
6. Dispatches Codex using `--yolo` in non-interactive mode for actionable events.
7. Probes `HEALTH_CHECK_URL` on a random 5-10 second interval for 10 attempts.
8. On repeated failures, builds a multi-log context bundle and dispatches a focused Codex recovery attempt.
9. Restarts `dotnet watch` automatically with exponential backoff.

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
- `WATCH_LOG_FILE` (default: `data/errors.log`)
- `RESTART_DELAY_SECONDS` (default `3`)
- `RESTART_MAX_DELAY_SECONDS` (default `30`)
- `UNIFIED_ERROR_LOG`
- `DOTNET_WATCH_STDOUT_LOG`
- `DOTNET_WATCH_STDERR_LOG`
- `AUTOFIX_DISPATCH_LOG`
- `AUTOFIX_STATE_FILE`
- `CODEX_CLI` (default `codex`)
- `CODEX_CLI_ARGS` (optional additional args)
- `HEALTH_CHECK_ENABLED` (default `true`)
- `HEALTH_CHECK_URL` (default `https://192.168.10.101:5266/health`)
- `HEALTH_CHECK_MAX_ATTEMPTS` (default `10`)
- `HEALTH_CHECK_MIN_INTERVAL_SECONDS` (default `5`)
- `HEALTH_CHECK_MAX_INTERVAL_SECONDS` (default `10`)
- `HEALTH_CHECK_TIMEOUT_SECONDS` (default `5`)
- `HEALTH_CONTEXT_LINES` (default `120`)
- `HEALTH_FIX_COOLDOWN_SECONDS` (default `90`)
