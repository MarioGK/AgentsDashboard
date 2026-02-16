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
- Active harness adapters: `CodexAdapter`, `OpenCodeAdapter`, `ClaudeCodeAdapter`, `ZaiAdapter`.
- Blazor components: 38 `.razor` files under `src/AgentsDashboard.ControlPlane/Components`.

## Architecture Rules

- Service boundary is MagicOnion service boundaries and application services; route-based HTTP APIs are not part of the target architecture.
- All communication between `ControlPlane` and `WorkerGateway` MUST use MagicOnion; do not use direct REST/gRPC or raw transport calls between these services.
- Repository is the primary orchestration boundary; do not reintroduce project-scoped workflow/state APIs.
- Enforce layered dependencies: `Domain -> Application -> Infrastructure -> UI` (inward only).
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs only at integration boundaries.

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

### EF Core (Mandatory)

- Async-only EF APIs (`*Async`) for queries and saves.
- One scoped `DbContext` per request/operation.
- Never share `DbContext` across threads.
- Use optimistic concurrency tokens for mutable hot-path entities.
- Use transactions for multi-step state transitions.
- Pass cancellation tokens through data-access calls.
- Startup should auto-apply migrations and idempotent seed data.
- Create migrations with `dotnet ef` tooling; do not hand-edit migration files or snapshots.

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

## Build & Test Quick Commands

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# CI format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error
```

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
- Repository creation is git-bound and folder-bound: a repo must include `GitUrl` + absolute `LocalPath`, and create flow validates/links/clones before persistence.
- ControlPlane exposes host folder browsing + create-folder in repository creation UI and stores git status (`CurrentBranch`, ahead/behind, staged/modified/untracked, scan/fetch timestamps, sync error).
- Git metrics refresh is hybrid: on repository detail open, explicit refresh action, and background refresh for recently viewed repositories.
- Git auth paths are system git-native: URL credentials, repository GitHub token (when configured), and host SSH keys/agent.
- Harness-only execution (no direct provider APIs).
- ControlPlane is the parent orchestrator and spawns worker-gateway containers on demand via Docker socket.
- Worker gateways launch ephemeral harness Docker containers.
- Worker pool is elastic (1..N) using `Orchestrator:Workers:*` settings for capacity, startup timeout, idle scale-down, and connectivity mode; runtime enforces single-slot workers (`MaxSlots=1`).
- Worker allocation is single-occupancy: each dispatch or terminal session gets a dedicated worker container instead of reusing an idle worker.
- Worker teardown is destructive: worker containers are removed after recycle/stop, and per-worker storage volumes (`worker-artifacts-*`, `worker-workspaces-*`) are removed with the worker.
- Terminal bridge is worker-aware: ControlPlane opens/reuses MagicOnion terminal hubs per worker and replays session output from terminal audit events on client reattach.
- Terminal mode is standalone-only in single-occupancy mode: terminal sessions launch their own dedicated worker and standalone harness container (run-attached terminal sessions are disabled).
- Worker image bootstrap runs on ControlPlane startup: if `Orchestrator:Workers:ContainerImage` is missing locally, ControlPlane attempts local build first and then pull fallback, and ControlPlane startup fails fast if the image is still unavailable.
- Runtime orchestrator behavior is policy-driven from persisted system settings (`SystemSettingsDocument.Orchestrator`) with short-lived cache and hot-reload invalidation on save.
- New dedicated settings page `/settings/orchestrator` controls worker pool sizing, admission thresholds, image policy/order, pull/build concurrency, start-failure budgets, cooldowns, draining/recycle behavior, and worker resource guardrails.
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
- Standalone terminal sessions inherit provider API key environment variables from the worker process (`CODEX_API_KEY` is mirrored to `OPENAI_API_KEY` when needed).
- Dashboard AI generation features (image Dockerfile generation and repository task prompt generation) now call `LlmTornadoGatewayService` with `ChatModel.Zai.Glm.Glm5`.
- Prompt skills support global + repository scopes via `PromptSkills`, with management in `/settings/skills` (global) and repository tasks, and slash-trigger autocomplete (`/`) in the task prompt Monaco editor.
- Artifact storage path is configurable via `Orchestrator:ArtifactsRootPath` (default `/data/artifacts`; development override `data/artifacts`).
- Health endpoints use split probes: `/alive` (liveness), `/ready` (dependency readiness), and `/health` (readiness alias with detailed JSON payload).

## Container Notes

- Service Dockerfiles (`ControlPlane`, `WorkerGateway`) must copy root build metadata files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`) before `dotnet restore`.
- `ControlPlane` runtime image includes `curl` for compose health checks and pre-creates `/data`, `/artifacts`, `/workspaces`.

## Last Verified

- Date: 2026-02-16
- Purpose: Date-only freshness marker for this document; do not use this section as a changelog/history log.
