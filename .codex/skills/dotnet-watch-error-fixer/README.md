# Dotnet watch auto-fix skill

This skill keeps the repository running under `dotnet watch`, auto-restarts on exits/rude edits, and triggers non-interactive Codex recovery attempts from relevant logs and health-check failures.

## Start

```bash
bash .codex/skills/dotnet-watch-error-fixer/launch-and-fix.sh
```

## What happens

1. Cleans existing `*.log` files in `data/logs` at startup.
2. Stops existing `dotnet watch` processes before startup.
3. Starts `dotnet watch --project src/AgentsDashboard.ControlPlane` with non-interactive + auto-restart rude-edit env.
4. Watches `data/errors.log` plus `dotnet-watch` output logs for actionable events.
5. Writes normalized events to `data/logs/autofix-unified-errors.log`.
6. Probes `https://192.168.10.101:5266/health` repeatedly (default 5-10s interval, 10 attempts).
7. If health checks fail `HEALTH_CHECK_MAX_ATTEMPTS` times, it dispatches a Codex recovery with context bundles from available logs.
8. Watches `dotnet watch --non-interactive --project "$WATCH_PROJECT"` and restarts with exponential backoff.

## Restart mode (always restart on rude edits)

The reliable startup command is:

```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development dotnet watch --non-interactive --project src/AgentsDashboard.ControlPlane
```

This avoids interactive prompts and matches how `dotnet watch` handles rude edits in unattended mode.

## Logs

- `data/logs/autofix-unified-errors.log` - unified event stream consumed by the dispatcher.
- `/tmp/logs/autofix-dispatch.log` - dispatch attempts and outcomes.
- `data/logs/autofix-state.log` - dedupe metadata.
- `data/logs/dotnet-watch.stdout.log` and `data/logs/dotnet-watch.stderr.log` - watch output.

## Environment

- `REPO_ROOT` (default: repo root)
- `WATCH_PROJECT` (default: `src/AgentsDashboard.ControlPlane`)
- `WATCH_LOG_FILE` (override default source log path if needed)
- `MONITOR_NORMAL_LOGS` (`true|false`, default `false`)
- `FIX_COOLDOWN_SECONDS` (default `10`)
- `CODEX_CLI` (default `codex`)
- `CODEX_CLI_ARGS` (optional extra args)
- `RESTART_DELAY_SECONDS` (default `3`)
- `RESTART_MAX_DELAY_SECONDS` (default `30`)
- `DOTNET_WATCH_STDOUT_LOG` (default `data/logs/dotnet-watch.stdout.log`)
- `DOTNET_WATCH_STDERR_LOG` (default `data/logs/dotnet-watch.stderr.log`)
- `AUTOFIX_DISPATCH_LOG` (default `/tmp/logs/autofix-dispatch.log`)
- `AUTOFIX_STATE_FILE` (default `data/logs/autofix-state.log`)
- `HEALTH_CHECK_ENABLED` (`true|false`, default `true`)
- `HEALTH_CHECK_URL` (default `https://192.168.10.101:5266/health`)
- `HEALTH_CHECK_MAX_ATTEMPTS` (default `10`)
- `HEALTH_CHECK_MIN_INTERVAL_SECONDS` (default `5`)
- `HEALTH_CHECK_MAX_INTERVAL_SECONDS` (default `10`)
- `HEALTH_CHECK_TIMEOUT_SECONDS` (default `5`)
- `HEALTH_CONTEXT_LINES` (default `120`)
- `HEALTH_FIX_COOLDOWN_SECONDS` (default `90`)
