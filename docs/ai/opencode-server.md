# OpenCode Server Runtime

## Purpose

`AgentsDashboard.TaskRuntime` runs OpenCode tasks through OpenCode server APIs and SSE events.
ControlPlane and TaskRuntime communication stays on MagicOnion contracts.

## Runtime Topology

1. ControlPlane dispatches a run with harness `opencode` over `ITaskRuntimeService.DispatchJobAsync`.
2. `HarnessExecutor` selects `OpenCodeSseRuntime`.
3. Runtime either:
   - connects to an externally configured OpenCode server, or
   - starts `opencode serve` locally for the run.
4. Runtime uses HTTP API for session lifecycle and SSE `/event` stream for deltas/status.
5. Runtime waits for idle terminal state, collects messages/diff, and returns a structured envelope.

## API Usage

- `GET /global/health`
- `POST /session`
- `POST /session/{id}/prompt_async`
- `GET /event`
- `GET /session/status`
- `GET /session/{id}/message`
- `GET /session/{id}/diff`

## Event Mapping

- OpenCode normalized event payload schema: `opencode.sse.v1`
- `message.part.delta` -> `assistant.delta`
- `session.diff` -> `diff.update`
- session status transitions are emitted as log/structured events until idle

## Environment Variables

### Required for provider auth

- `OPENCODE_API_KEY`
- provider-specific credentials passed through ControlPlane secret mapping

### Server connection

- `OPENCODE_SERVER_BASE_URL` or `OPENCODE_SERVER_URL` for external server
- `OPENCODE_SERVER_USERNAME`
- `OPENCODE_SERVER_PASSWORD`

### Local server bootstrap

- `OPENCODE_SERVER_HOST` (default `127.0.0.1`)
- `OPENCODE_SERVER_PORT` (optional fixed port, otherwise dynamic free port)
- `OPENCODE_SERVER_STARTUP_TIMEOUT_SECONDS` (default `30`)

### Model/runtime policy

- `OPENCODE_MODEL` or `HARNESS_MODEL`
- `OPENCODE_PROVIDER`
- `OPENCODE_MODE`/`HARNESS_MODE`/`TASK_MODE`/`RUN_MODE`

## Failure Modes

- Health probe failure on configured server URL.
- Local server startup timeout or process crash.
- Session does not reach idle before timeout.
- API call returns non-success status.

Envelope metadata includes:

- `runtime=opencode-sse`
- `runtimeMode=sse`
- `sessionId`
- `transport=sse`
- status snapshots and final diff/message indicators

## Operations

### Verify opencode binary in runtime

```bash
opencode --version
opencode serve --help
```

### Manual local server smoke test

```bash
opencode serve --hostname 127.0.0.1 --port 43111
curl http://127.0.0.1:43111/global/health
```

## Non-goals

- No command runtime fallback for OpenCode runs.
- No unsupported harness routing through OpenCode runtime.
