# AI Orchestrator

Self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution runs through CLI harnesses (`codex`, `opencode`) and dashboard AI feature calls via `LlmTornado`.

## Solution Layout

```text
src/
  AgentsDashboard.ControlPlane        # Blazor Server UI, orchestration, SignalR
  AgentsDashboard.TaskRuntimeGateway  # MagicOnion runtime gateway, queue, harness execution
  AgentsDashboard.Contracts           # Shared domain + MagicOnion contracts

tests/
  AgentsDashboard.UnitTests
  AgentsDashboard.IntegrationTests
  AgentsDashboard.Playwright

deploy/
  harness-image/
  harness-images/

docs/
  ai/
```
## Architecture Rules

- All intra communication MUST use MagicOnion.
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs at integration boundaries only.
- Backward compatibility is not required; keep only intended current behavior.
- We dont have to care about database lost of data and etc.

## Engineering Conventions

### Code Style

- File-scoped namespaces.
- Primary constructors where appropriate.
- Required properties for DTOs.
- Async methods use `Async` suffix.
- Keep extension methods/helper types in separate files.
- One primary type per file.
- No comments unless explicitly requested.
- Warnings are errors (`TreatWarningsAsErrors=true`); do not disable with `NoWarn`.
- All extensions must be inside of an Extensions folder instead of the root of the project.
- There should not be single use interfaces. 
- Every record or class must be in a single file, dont create a single file with multiple records and clasess.

### LiteDB (Mandatory)

- Persistence is LiteDB-only (`LiteDB` NuGet `6.0.0-prerelease.75`);
- All store/repository APIs are async-only and must flow cancellation tokens.
- Use `IRepository<>` for collection access; do not reintroduce EF-style `DbContext` or single file database class.
- Serialize database access through the LiteDB execution path (`LiteDbDatabase`/`LiteDbExecutor`) for safe shutdown/disposal.
- Store run artifacts, workspace uploads, and image/file payloads in LiteDB (file storage + metadata), not filesystem paths.
- Workspace image ingestion compresses to WebP lossless before DB persistence and multimodal dispatch.

## Agent Workflow Rules

- Always use `main`; do not create feature branches.
- Assume that we dont have to save anything, this is a greenfield project and is not deployed yet.
- Build with `dotnet build src/AgentsDashboard.slnx -m --tl`.
- Use TUnit/MTP filtering for focused runs (`-- --treenode-filter ...` / `-- --filter-uid ...`).
- Use `dotnet build-server shutdown` if build behavior is inconsistent.
- Run `dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error` before commit.

## Local Run
- LAN: `http://192.168.10.101:5266` (HTTP in local dev)
- Health: `/alive`, `/ready`, `/health`
- Services must bind all interfaces (`0.0.0.0`) for LAN accessibility.
- Runtime persistence/log/workspace paths resolve under repo-root `data/`.

Preferred watch command:

```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5266 dotnet watch --no-launch-profile --project src/AgentsDashboard.ControlPlane
```

## Logging
- Use `ZLogger` in both ControlPlane and TaskRuntimeGateway.
- Prefer fewer, richer context lines over repetitive logs.

## Build & Test Quick Commands

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# Tests
dotnet test --solution src/AgentsDashboard.slnx

# Format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error

# Playwright full workflow gate (real Codex/OpenCode harness flow)
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_REPO_REMOTE_PATH=/abs/path/to/seeded-remote.git \
PLAYWRIGHT_E2E_REPO_CLONE_ROOT=/abs/path/to/clones \
npm test
```

MTP note: always pass `--project`, `--solution`, or direct `.csproj/.slnx`.

## Harnesses

| Harness | CLI | Provider |
|---|---|---|
| Codex | `codex` | OpenAI GPT |
| OpenCode | `opencode` | OpenCode |

## Execution Model

- Repository is the top-level scope.
- Run execution mode is first-class domain data (`Default`, `Plan`, `Review`).
- Runtime execution is policy-driven (`IHarnessRuntime`) and transport-aware.
- ControlPlane persistence runs on LiteDB with repository abstractions and no EF migration/bootstrap path.
- TaskRuntimeGateway emits structured events; ControlPlane persists projections (`RunStructuredEventDocument`, `RunDiffSnapshotDocument`, `RunToolProjectionDocument`).
- Artifact/file/image persistence is DB-backed through LiteDB file storage; run detail downloads stream from DB.
- Realtime typed updates remain enabled during log/status cutover.
- Startup/bootstrap non-critical operations run through `IBackgroundWorkCoordinator` with bounded concurrency and dedupe.
- Runtime image policy/settings are orchestrator-driven (`SystemSettingsDocument.Orchestrator`) with runtime cache + invalidation.
- Image ensure/rebuild is background work (`TaskRuntimeImageResolution`).
- Runtime operations/policy UI lives at `/settings/task-runtimes`.
- Runtime allocation is task-scoped; runtimes are reused per active task and stopped after inactivity timeout.
- Runtime lifecycle transitions are persisted into `TaskRuntimeDocument` (`Ready`, `Busy`, `Inactive`, `Failed`) with cold-start/inactivity aggregates shown on `/overview`.
- Semantic chunk search uses LiteDB-backed storage with in-process cosine fallback scoring.
- Task dispatch is singleton per task (one active/pending run; additional triggers queue).
- Successful changed runs auto-commit and push default branch; successful no-diff runs end `Obsolete`.
- Health probes are split: `/alive` (liveness), `/ready` (readiness), `/health` (readiness alias + payload).
