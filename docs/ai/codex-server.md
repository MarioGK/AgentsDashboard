# Codex Server Runtime

## Purpose

`AgentsDashboard.TaskRuntime` runs Codex tasks through Codex app-server over stdio JSON-RPC.
Task dispatch/control flow remains ControlPlane -> MagicOnion -> TaskRuntime.

## Runtime Topology

1. ControlPlane dispatches a run with harness `codex` over `ITaskRuntimeService.DispatchJobAsync`.
2. `HarnessExecutor` resolves runtime mode and selects `CodexAppServerRuntime`.
3. Runtime starts `codex app-server --listen stdio://`.
4. Runtime speaks JSON-RPC over stdio:
   - `initialize`
   - `thread/start`
   - `turn/start`
5. Runtime streams events into structured runtime event wire payloads.
6. Completion envelope is returned to ControlPlane and persisted.

## Event Mapping

- `item/reasoning/textDelta` and `item/reasoning/summaryTextDelta` -> `reasoning.delta`
- `item/agentMessage/delta` -> `assistant.delta`
- `item/commandExecution/outputDelta` -> `command.delta`
- `item/fileChange/outputDelta` and `turn/diff/updated` -> `diff.update`
- `turn/completed` -> completion event + envelope finalization

## Environment Variables

### Required for auth/provider

- `CODEX_API_KEY`
- `OPENAI_API_KEY` (mirrored when applicable by ControlPlane secret mapping)

### Runtime behavior

- `CODEX_APPROVAL_POLICY` (default `on-failure`, forced `never` for plan/review)
- `CODEX_SANDBOX` (default `danger-full-access`)
- `CODEX_MODEL` or `HARNESS_MODEL`
- `HARNESS_MODE`/`TASK_MODE`/`RUN_MODE` (`default`, `plan`, `review`)
- `CODEX_MAX_TOKENS` or `HARNESS_MAX_TOKENS`
- `CODEX_SERVER_*` variables are not used by the runtime launcher in this implementation.

## Failure Modes

- Server process fails before `codex app-server --listen stdio://` is active.
- Stdio I/O timeout or deadlock while waiting for JSON-RPC response.
- Stdio channel closes before `turn/completed`.
- JSON-RPC error response on request ID.
- Turn completes with non-`completed` status.

Each failure maps to a failed `HarnessResultEnvelope` with metadata fields including:

- `runtime=codex-stdio`
- `runtimeMode=stdio`
- `threadId`
- `turnId`
- `turnStatus`
- `stderr` (truncated)

## Operations

### Verify codex binary in runtime

```bash
codex --version
codex app-server --help
```

### Manual smoke test for local app-server

```bash
codex app-server --listen stdio://
```

Then send JSON-RPC `initialize` over stdin/stdout.

## Non-goals

- No fallback to command runtime.
- No non-codex harness execution via this runtime.
