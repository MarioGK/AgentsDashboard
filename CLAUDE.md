# AI Orchestrator - Development Context

Production-focused, self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution is via CLI harnesses (`codex`, `opencode`, `claude-code`, `zai`) and dashboard AI feature calls via `LlmTornado`.

## AGENTS Maintenance (Mandatory)

- `AGENTS.md` in this repo is a symlink to `CLAUDE.md`; edit `CLAUDE.md` only.
- `AGENTS.md` must always be kept up to date.
- Update this file only when a change is materially relevant to operating or developing this platform.
- Significant change includes architecture, domain model, workflow behavior, auth/policies, build/test commands, harness support, deployment, or project structure changes.
- For each major update, refresh the `Last Verified` date in this file.
- Do not append historical change logs in this file; keep it as current-state guidance only.

## Token Efficiency Rules (Mandatory)

- Keep this document concise and operational.
- Prefer stable rules and summaries over long inventories.
- Use counts and representative examples instead of exhaustive lists.
- Remove stale details instead of appending more history.
- Keep command sections minimal and CI-relevant.

## Solution Layout

```text
src/
  AgentsDashboard.ControlPlane    # Blazor Server UI, scheduler, SignalR, YARP
  AgentsDashboard.WorkerGateway   # gRPC worker, queue, harness execution
  AgentsDashboard.Contracts       # Shared domain + gRPC contracts

deploy/
  harness-image/                  # all-in-one harness image
  harness-images/                 # base + per-harness Dockerfiles
docs/
  ai/                             # local AI integration references for future agents
```

## Snapshot

- `src/AgentsDashboard.slnx`: 3 production projects (`AgentsDashboard.ControlPlane`, `AgentsDashboard.WorkerGateway`, `AgentsDashboard.Contracts`).
- Active harness runtimes: `CodexAppServerRuntime`, `OpenCodeSseRuntime`, `ClaudeStreamRuntime`, `ZaiClaudeCompatibleRuntime` with `command` fallback.
- Harness adapters: `CodexAdapter`, `OpenCodeAdapter`, `ClaudeCodeAdapter`, `ZaiAdapter`.
- Blazor components: 39 `.razor` files under `src/AgentsDashboard.ControlPlane/Components`.

## Architecture Rules

- Service boundary is MagicOnion service boundaries and application services; route-based HTTP APIs are not part of the target architecture.
- All communication between `ControlPlane` and `WorkerGateway` MUST use MagicOnion; do not use direct REST/gRPC or raw transport calls between these services.
- Repository is the primary orchestration boundary; do not reintroduce project-scoped workflow/state APIs.
- Enforce layered dependencies: `Domain -> Application -> Infrastructure -> UI` (inward only).
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs only at integration boundaries.
- Backward compatibility is not required. Implement and maintain only the current intended behavior and new code paths.

## Product Model

1. Repository (top-level workspace)
2. Task
3. Run
4. Finding
5. Agent
6. WorkflowV2 (DAG)
7. WorkflowExecutionV2
8. WorkflowDeadLetter

## Engineering Conventions

### Code Style

- File-scoped namespaces.
- Primary constructors where appropriate.
- Required properties for DTOs.
- Async methods use `Async` suffix.
- No comments unless explicitly requested.
- Warnings are errors by default: `TreatWarningsAsErrors=true` in `Directory.Build.props`; never set `TreatWarningsAsErrors` to false.
- Never add `<NoWarn>` in `.csproj`, `.cdproj`, or `.props` files to bypass warning-as-error enforcement.

### EF Core (Mandatory)

- Async-only EF APIs (`*Async`) for queries and saves.
- One scoped `DbContext` per request/operation.
- Never share `DbContext` across threads.
- Use optimistic concurrency tokens for mutable hot-path entities.
- Use transactions for multi-step state transitions.
- Pass cancellation tokens through data-access calls.
- Startup should auto-apply migrations and idempotent seed data.
- Create migrations only with the `dotnet ef` CLI (`dotnet ef migrations add ...`, `dotnet ef database update`, `dotnet ef migrations remove`, etc.); do not hand-edit migration files or snapshots.

### Authentication

- Cookie auth with roles: `viewer`, `operator`, `admin`.
- Enforce policies at service boundaries and Blazor pages.

## Agent Workflow Rules

- Always use `main`; do not create feature branches.
- Build with `dotnet build src/AgentsDashboard.slnx -m --tl`.
- Use TUnit/MTP filtering for focused runs (`-- --treenode-filter ...` / `-- --filter-uid ...`).
- Use `dotnet build-server shutdown` if builds behave unexpectedly.
- Run `dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error` before commit.

## Local Run

```bash
docker compose up -d

dotnet run --project src/AgentsDashboard.WorkerGateway
dotnet run --project src/AgentsDashboard.ControlPlane
```

- Dashboard: `http://localhost:5266`
- LAN: `http://192.168.10.101:5266` (explicitly HTTP; HTTPS is not enabled in local watch/build unless you add a reverse proxy or dev cert).
- Health: `curl http://localhost:5266/health`, `curl http://localhost:5266/ready`, and `curl http://localhost:5266/alive`
- LAN accessibility is mandatory: when started, services must bind to `0.0.0.0` (or equivalent all-interfaces binding), not localhost-only.
- Preferred watch command (LAN + polling watcher + explicit reload host):
```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5266 dotnet watch --no-launch-profile --project src/AgentsDashboard.ControlPlane
```

## Logging

- Logging runs through `ZLogger` for both ControlPlane and WorkerGateway.
- Default format is plain text with friendly, human-readable output and no JSON.
- Logging uses ZLogger plain-text formatter defaults in both host `Program.cs`.
- Use interpolation variables (or equivalent template pairs) in `ZLog*` calls to keep logs structured (`... {RunId}`, `{WorkerId}`, `{QueueDepth}`, `{DurationMs}`).
- Prefer fewer log lines with richer context over repetitive one-off lines in hot paths.
- Do not log secrets, credentials, or full tokens; include only IDs and derived metadata.

## Build & Test Quick Commands

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# Test
dotnet test --solution src/AgentsDashboard.slnx

# CI format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error
```

- GitHub Actions workflows are temporarily disabled in this repository and will be fixed/re-enabled later.

MTP note: always pass `--project`, `--solution`, or a direct `.csproj/.slnx`; `dotnet test <folder>` is not supported in this repo setup.

## Harnesses

| Harness | CLI | Provider |
|---|---|---|
| Codex | `codex` | OpenAI GPT |
| OpenCode | `opencode` | OpenCode |
| Claude Code | `claude-code` | Anthropic Claude |
| Zai | `zai` | Zhipu GLM-5 |

## Execution Model

- Repository is the only top-level scope (project entity removed).
- Run execution modes are first-class domain data: `HarnessExecutionMode` (`Default`, `Plan`, `Review`) persisted as task default (`TaskDocument.ExecutionModeDefault`) and run effective mode (`RunDocument.ExecutionMode`), with per-run structured protocol marker (`RunDocument.StructuredProtocol`).
- Worker execution is runtime-driven (`IHarnessRuntime`) with transport selection by harness/runtime policy, not parser-first command heuristics.
- Codex primary transport is `codex app-server` with `command` fallback on runtime failure.
- OpenCode primary/only managed transport is `opencode serve` + HTTP/SSE (`/session`, `/event`, `/session/{id}/message`, `/session/{id}/diff`); no primary OpenCode CLI run mode.
- Claude transport is CLI `--output-format stream-json`; Z.ai uses Claude-compatible stream path with strict `glm-5` enforcement.
- Worker emits normalized structured events (v2 canonical payload) with ordered sequence/category/schema metadata; ControlPlane persists and projects these via `RunStructuredEventDocument`, `RunDiffSnapshotDocument`, and `RunToolProjectionDocument`.
- ControlPlane realtime publishes typed structured events (`RunStructuredEventChangedEvent`, `RunDiffUpdatedEvent`, `RunToolTimelineUpdatedEvent`) while retaining existing log/status event paths during cutover.
- Run detail UX includes structured panes and Monaco diff rendering (`RunDiffViewer` / `RunDiffFileList`) with side-by-side diff defaults and patch export actions.
- Diff viewer layout is session-toggleable between side-by-side and inline while keeping side-by-side as default.
- Workspace prompt submission supports mode override and agent-team execution (`WorkspaceAgentTeamRequest`) with parallel member lanes and optional synthesis.
- Agent-team parallel stages persist lane diff telemetry, merged non-conflicting patch, and conflict metadata in `WorkflowStageResult.AgentTeamDiff`; synthesis prompts include this conflict context.
- Repository creation is git-bound and folder-bound: a repo must include `GitUrl` + absolute `LocalPath`, and create flow validates/links/clones before persistence.
- ControlPlane exposes host folder browsing + create-folder in repository creation UI and stores git status (`CurrentBranch`, ahead/behind, staged/modified/untracked, scan/fetch timestamps, sync error).
- Git metrics refresh is hybrid: on repository detail open, explicit refresh action, and background refresh for recently viewed repositories.
- Git auth paths are system git-native: URL credentials, repository GitHub token (when configured), and host SSH keys/agent.
- Harness-only execution (no direct provider APIs).
- ControlPlane is the parent orchestrator and spawns worker-gateway containers on demand via Docker socket.
- Worker gateways launch ephemeral harness Docker containers.
- Worker pool is elastic (1..N) using `Orchestrator:Workers:*` settings for capacity, startup timeout, idle scale-down, and connectivity mode; runtime enforces single-slot workers (`MaxSlots=1`).
- Worker allocation is single-occupancy: each dispatch gets a dedicated worker container instead of reusing an idle worker.
- Worker teardown is destructive for compute and artifacts: worker containers are removed after recycle/stop and per-worker artifact volumes (`worker-artifacts-*`) are removed; `/workspaces` is mounted from a shared persistent volume (`agentsdashboard-workspaces` by default, override via `WORKER_SHARED_WORKSPACES_BIND`).
- Task git execution uses persistent task-scoped checkouts under `/workspaces/repos/{repositoryId}/tasks/{taskId}`. Container sandboxing is the isolation boundary; git worktrees are not used.
- Task dispatch enforces singleton queue semantics per task: at most one active/pending run per task; additional triggers remain queued and the next queued run is dispatched when the current run reaches a terminal state.
- Successful changed runs auto-commit and push the default branch; successful no-diff runs are terminal `Obsolete`.
- Task list surfaces (repository, schedules, workflow task lists) show status chips based on latest run state with spinner for working states (`Queued`, `Running`, `PendingApproval`).
- Worker image bootstrap runs on ControlPlane startup: if `Orchestrator:Workers:ContainerImage` is missing locally, ControlPlane attempts local build first and then pull fallback, and ControlPlane startup fails fast if the image is still unavailable.
- Runtime orchestrator behavior is policy-driven from persisted system settings (`SystemSettingsDocument.Orchestrator`) with short-lived cache and hot-reload invalidation on save.
- New dedicated settings page `/settings/orchestrator` controls worker pool sizing, admission thresholds, image policy/order, pull/build concurrency, start-failure budgets, cooldowns, draining/recycle behavior, and worker resource guardrails.
- ControlPlane runs background task-retention cleanup with distributed lease coordination (`maintenance-task-cleanup`): age-based deletion (last-activity policy) and DB-size pressure cleanup (`DbSizeSoftLimitGb` -> `DbSizeTargetGb`) with full task-cascade deletion (runs/logs/findings/prompt history/AI summaries/semantic chunks/artifact directories) and optional post-pressure `VACUUM`.
- Cleanup cycle also prunes structured run payload tables for old terminal runs via `PruneStructuredRunDataAsync`, honoring workflow/open-finding exclusion flags used by task cleanup eligibility.
- ControlPlane now supports DB-backed distributed leases (`Leases`) for multi-instance coordination of scale-out, image resolution, and reconciliation operations.
- Harness image bootstrap runs on WorkerGateway startup: missing configured images attempt local build from known harness Dockerfiles before pull fallback.
- Worker tool diagnostics are worker-scoped: ControlPlane queries each WorkerGateway via MagicOnion (`GetHarnessToolsAsync`) and renders tool/version status in `/settings/workers/{workerId}`.
- Provider secrets are injected into run environment variables and the secrets map; Codex credentials map to both `CODEX_API_KEY` and `OPENAI_API_KEY`.
- Added provider mapping `llmtornado` for Z.ai Anthropic-compatible routing: maps to `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_API_KEY`, `ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic`, and `Z_AI_API_KEY`.
- Provider fallback now supports global `llmtornado` secret scope (`repositoryId=global`) when repository-level Anthropic/Z.ai credentials are not present.
- GLM policy is strict for Z.ai paths: `zai` harness and Claude-Code-via-Z.ai flows force model `glm-5`; no automatic fallback model is used.
- If repository Codex secrets are missing, ControlPlane attempts host credential discovery from `OPENAI_API_KEY`/`CODEX_API_KEY` environment variables, then `CODEX_HOME/auth.json` or `~/.codex/auth.json`.
- Standard JSON result envelope with normalized fallback parsing.
- Redact secrets from output.
- Dashboard AI generation features (image Dockerfile generation and repository task prompt generation) now call `LlmTornadoGatewayService` with `ChatModel.Zai.Glm.Glm5`.
- Prompt skills support global + repository scopes via `PromptSkills`, with management in `/settings/skills` (global) and repository tasks, and slash-trigger autocomplete (`/`) in the task prompt Monaco editor.
- Dedicated global search page `/search` provides cross-repository search over tasks/runs/findings/run logs with filterable scope (repository/task/kind/time/state), combining keyword scoring with semantic chunk ranking via embeddings + sqlite-vec when available, with text/heuristic fallback when vector support is unavailable.
- Artifact storage path is configurable via `Orchestrator:ArtifactsRootPath` (default `/data/artifacts`; development override `data/artifacts`).
- Health endpoints use split probes: `/alive` (liveness), `/ready` (dependency readiness), and `/health` (readiness alias with detailed JSON payload).

## Container Notes

- Service Dockerfiles (`ControlPlane`, `WorkerGateway`) must copy root build metadata files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`) before `dotnet restore`.
- `ControlPlane` runtime image includes `curl` for compose health checks and pre-creates `/data`, `/artifacts`, `/workspaces`.

## Last Verified

- Date: 2026-02-17
- Purpose: Date-only freshness marker for this document; do not use this section as a changelog/history log.
