# AI Orchestrator - Development Context

Production-focused, self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution runs through CLI harnesses (`codex`, `opencode`, `claude-code`, `zai`) and dashboard AI feature calls via `LlmTornado`.

## AGENTS Maintenance (Mandatory)

- `AGENTS.md` in this repo is a symlink to `CLAUDE.md`; edit `CLAUDE.md` only.
- Keep this document current whenever architecture, domain model, workflow behavior, auth/policies, build/test commands, harness support, deployment, or project structure changes.
- Update `Last Verified` for major updates.
- Keep this file as current-state guidance only; do not append historical change logs.

## Token Efficiency Rules (Mandatory)

- Keep this document concise and operational.
- Prefer stable rules over exhaustive inventories.
- Remove stale details rather than appending historical drift.
- Keep command sections minimal and CI-relevant.

## Solution Layout

```text
src/
  AgentsDashboard.ControlPlane        # Blazor Server UI, orchestration, SignalR, YARP
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

## Snapshot

- `src/AgentsDashboard.slnx` includes `ControlPlane`, `TaskRuntimeGateway`, `Contracts`, plus unit/integration test projects.
- Active harness runtimes: `CodexAppServerRuntime`, `OpenCodeSseRuntime`, `ClaudeStreamRuntime`, `ZaiClaudeCompatibleRuntime` with command fallback.
- Harness adapters: `CodexAdapter`, `OpenCodeAdapter`, `ClaudeCodeAdapter`, `ZaiAdapter`.

## Architecture Rules

- Service boundary is MagicOnion service boundaries and application services; route-based HTTP APIs are not the target architecture.
- All `ControlPlane` ↔ `TaskRuntimeGateway` communication MUST use MagicOnion.
- Repository is the top-level orchestration boundary.
- Enforce layered dependencies: `Domain -> Application -> Infrastructure -> UI`.
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs at integration boundaries only.
- Backward compatibility is not required; keep only intended current behavior.

## Product Model

1. Repository
2. Task
3. Run
4. Finding
5. Agent
6. WorkflowV2
7. WorkflowExecutionV2
8. WorkflowDeadLetter

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

### LiteDB (Mandatory)

- Persistence is LiteDB-only (`LiteDB` NuGet `6.0.0-prerelease.75`); EF Core/SQLite/migrations are removed.
- All store/repository APIs are async-only and must flow cancellation tokens.
- Use `IRepository<>` for collection access; do not reintroduce EF-style `DbContext`.
- Serialize database access through the LiteDB execution path (`LiteDbDatabase`/`LiteDbExecutor`) for safe shutdown/disposal.
- Store run artifacts, workspace uploads, and image/file payloads in LiteDB (file storage + metadata), not filesystem paths.
- Workspace image ingestion compresses to WebP lossless before DB persistence and multimodal dispatch.

### Authentication

- Cookie auth with roles: `viewer`, `operator`, `admin`.
- Enforce policies at service boundaries and Blazor pages.

## Agent Workflow Rules

- Always use `main`; do not create feature branches.
- Build with `dotnet build src/AgentsDashboard.slnx -m --tl`.
- Use TUnit/MTP filtering for focused runs (`-- --treenode-filter ...` / `-- --filter-uid ...`).
- Use `dotnet build-server shutdown` if build behavior is inconsistent.
- Run `dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error` before commit.

## Local Run

```bash
docker compose up -d

dotnet run --project src/AgentsDashboard.TaskRuntimeGateway
dotnet run --project src/AgentsDashboard.ControlPlane
```

- Dashboard: `http://localhost:5266`
- LAN: `http://192.168.10.101:5266` (HTTP in local dev)
- Health: `/alive`, `/ready`, `/health`
- Services must bind all interfaces (`0.0.0.0`) for LAN accessibility.

Preferred watch command:

```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5266 dotnet watch --no-launch-profile --project src/AgentsDashboard.ControlPlane
```

## Logging

- Use `ZLogger` in both ControlPlane and TaskRuntimeGateway.
- Default format: UTC timestamp + short level + service.
- Prefer fewer, richer context lines (`ZLog*Object`) over repetitive logs.
- Never log secrets or full credentials/tokens.
- Logging setup:
  - `src/AgentsDashboard.ControlPlane/Logging/HostLoggingExtensions.cs`
  - `src/AgentsDashboard.TaskRuntimeGateway/Logging/HostLoggingExtensions.cs`

## Build & Test Quick Commands

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# Tests
dotnet test --solution src/AgentsDashboard.slnx

# Format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error

# Playwright full workflow gate (real Z.ai harness)
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_ZAI_API_KEY=... \
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
| Claude Code | `claude-code` | Anthropic Claude |
| Zai | `zai` | Zhipu GLM-5 |

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
- Semantic chunk search uses LiteDB-backed storage with in-process cosine fallback scoring instead of sqlite-vec.
- Task dispatch is singleton per task (one active/pending run; additional triggers queue).
- Successful changed runs auto-commit and push default branch; successful no-diff runs end `Obsolete`.
- Health probes are split: `/alive` (liveness), `/ready` (readiness), `/health` (readiness alias + payload).

## Container Notes

- Service Dockerfiles must copy root build metadata (`global.json`, `Directory.Build.props`, `Directory.Packages.props`) before `dotnet restore`.
- ControlPlane runtime image includes `curl` and pre-creates `/data`, `/artifacts`, `/workspaces`.

## Last Verified

- Date: 2026-02-18
- Purpose: date-only freshness marker for this document.
