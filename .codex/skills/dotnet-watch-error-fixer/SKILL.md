---
name: dotnet-watch-error-fixer
description: Launches dotnet watch and continuously auto-fixes log events via non-interactive Codex sessions for this repository.
---

# dotnet-watch-error-fixer

Use this skill when you want continuous `dotnet watch` operation and automated recovery actions for this repository.

This launcher is repo-specific and intentionally not configurable:
- project is fixed to `src/AgentsDashboard.ControlPlane`
- single unified log file is `/tmp/autofix/logs/autofix.log`

## What this skill does

1. Enforces launcher singleton lock (`/tmp/autofix/launcher.lock`) so only one launcher process runs.
2. Stops existing `dotnet watch` and releases stale listeners on startup ports before launch.
3. Starts:
   `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development dotnet watch --non-interactive --project src/AgentsDashboard.ControlPlane`
4. Watches warning/error-like events from the unified log stream.
5. Classifies events into `compile/runtime/startup` taxonomy.
6. Applies compile-loop dispatch gating to avoid duplicate dispatch storms.
7. Dispatches Codex using `--yolo` in non-interactive mode for actionable events.
8. Restarts `dotnet watch` automatically with exponential backoff.

## Required behavior

- Non-interactive, no manual prompts.
- Long-lived run-loop until explicit stop.
- Repo-scoped actions only.
- Tail one log file:

```bash
tail -F /tmp/autofix/logs/autofix.log
```

## Default command

```bash
bash .codex/skills/dotnet-watch-error-fixer/launch-and-fix.sh
```

## Environment knobs

None. Configuration values are fixed in `.codex/skills/dotnet-watch-error-fixer/launch-and-fix.sh`.
