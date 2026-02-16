# AI Orchestrator - Development Context

Production-focused, self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution is via CLI harnesses (`codex`, `opencode`, `claude-code`, `zai`) and dashboard AI feature calls via `LlmTornado`.

## AGENTS Maintenance (Mandatory)

- `AGENTS.md` in this repo is a symlink to `CLAUDE.md`; edit `CLAUDE.md` only.
- `AGENTS.md` must always be kept up to date.
- After every significant change, update this file before finishing work.
- Significant change includes architecture, domain model, workflow behavior, auth/policies, build/test commands, harness support, deployment, or project structure changes.
- For each major update, refresh the `Last Verified` date in this file.

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
- Blazor components: 41 `.razor` files under `src/AgentsDashboard.ControlPlane/Components`.

## Architecture Rules

- Service boundary is MagicOnion service boundaries and application services; route-based HTTP APIs are not part of the target architecture.
- All communication between `ControlPlane` and `WorkerGateway` MUST use MagicOnion; do not use direct REST/gRPC or raw transport calls between these services.
- Enforce layered dependencies: `Domain -> Application -> Infrastructure -> UI` (inward only).
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs only at integration boundaries.

## Product Model

1. Project
2. Repository
3. Task
4. Run
5. Finding
6. Agent
7. WorkflowV2 (DAG)
8. WorkflowExecutionV2
9. WorkflowDeadLetter

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
- Artifact storage path is configurable via `Orchestrator:ArtifactsRootPath` (default `/data/artifacts`; development override `data/artifacts`).
- Health endpoints use split probes: `/alive` (liveness), `/ready` (dependency readiness), and `/health` (readiness alias with detailed JSON payload).

## Container Notes

- Service Dockerfiles (`ControlPlane`, `WorkerGateway`) must copy root build metadata files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`) before `dotnet restore`.
- `ControlPlane` runtime image includes `curl` for compose health checks and pre-creates `/data`, `/artifacts`, `/workspaces`.

## Last Verified

- Date: 2026-02-16
- Fixed worker tool diagnostics compatibility for stale worker images: ControlPlane startup image bootstrap now forces policy-based refresh (with local fallback if refresh fails), and `/settings/workers/{workerId}` now shows a targeted legacy-image message when `GetHarnessToolsAsync` is unimplemented instead of surfacing the raw RPC exception.
- Added worker spawn hardening in ControlPlane: `DockerWorkerLifecycleManager` now ensures the configured Docker network exists and creates it when missing before creating worker containers, preventing `network agentsdashboard not found` startup failures in fresh environments.
- Container Image Builder `Full Harness` template now mirrors `deploy/harness-image/Dockerfile` exactly so dashboard template builds match repo harness image content.
- Worker capacity defaults are `MinWorkers=4` and `MaxWorkers=100`; runtime now honors persisted `Orchestrator.MinWorkers` and applies a `>= 1` floor when stored values are invalid (`0`).
- 2026-02-16 run with `dotnet watch` on ControlPlane + WorkerGateway and fixed startup null-safety issues in dispatch execution.
- Clarified LAN launch contract: services must be reachable at `http://192.168.10.101:5266` (HTTP explicit) with `0.0.0.0` binding; HTTPS is not enabled by default.
- Enforced strict single-occupancy worker execution: dispatch now provisions a fresh worker per task/terminal lease, with `Worker__MaxSlots=1` and WorkerGateway queue/session limits clamped to one active job/session.
- Worker recycle now removes containers and worker-scoped storage volumes; ControlPlane worker host binds now use per-worker volume names (`worker-artifacts-{workerId}`, `worker-workspaces-{workerId}`).
- Run completion now triggers worker recycle automatically; standalone terminal session closure (explicit close, worker-close event, or grace timeout) also recycles the owning worker.
- Added required task prompt envelope support from `SystemSettingsDocument.Orchestrator.TaskPromptPrefix/TaskPromptSuffix`, with enforced default git branch/commit/push instructions when settings are empty.
- Run dispatch now sets deterministic per-run task branches (`agent/<repo>/<task>/<runId>`) and passes `TASK_BRANCH`, `TASK_DEFAULT_BRANCH`, `PR_BRANCH`, and `PR_BRANCH_PREFIX` to harness execution.
- `RunDispatcher` layered prompts now support per-task prompt wrappers via task instruction file names `prompt-prefix` / `task-prompt-prefix` and `prompt-suffix` / `task-prompt-suffix`.
- PR branch validation now honors a dispatcher-provided `PR_BRANCH_PREFIX` override so harness-side PR automation validates task-owned branch patterns correctly.
- Added `LlmTornado` package integration in ControlPlane and introduced `LlmTornadoGatewayService` for dashboard AI features.
- Provider settings now include a global `LlmTornado` key section (`repositoryId=global`) plus repository-level `llmtornado` provider support.
- `RunDispatcher` now maps `llmtornado` secrets into Anthropic-compatible Z.ai env vars and applies global-secret fallback.
- Enforced `glm-5` for Z.ai/Claude-Code-via-Z.ai execution settings and dashboard AI generation paths.
- Added offline docs knowledge base under `docs/ai` with Z.ai + Claude Code setup, LlmTornado integration notes, GLM-5 policy, and source index.
- Refreshed `docs/ai` from official references, added `docs/ai/README.md` index, and added `docs/ai/feature-ideas.md` as the next-step AI backlog for agents/workflows/images.
- Added `OrchestratorSettings` under `SystemSettingsDocument` and persisted migration `20260216151028_OrchestratorReliabilityV2` (new `Settings.Orchestrator`, run image provenance fields, and `Leases` table).
- `DockerWorkerLifecycleManager` now enforces runtime-configurable policies for min/max workers, per-worker concurrency, image resolution policy, build/pull throttling, failure budgets/cooldowns, draining, auto-recycle, and reconciler sync.
- Added `IOrchestratorRuntimeSettingsProvider`, `ILeaseCoordinator`, and `WorkerPoolReconciliationService` to support reliable runtime orchestration and multi-instance-safe worker operations.
- Added `/settings/orchestrator` UI and operator action center (pause/resume scale-out, clear cooldown, ensure image, drain/recycle workers, run reconciliation).
- Worker image rollout now supports canary behavior via `Orchestrator.WorkerCanaryImage` + `Orchestrator.CanaryPercent` with automatic fallback to base image when canary resolution fails.
- Worker container host config now enforces `nofile` ulimit when `Orchestrator.WorkerFileDescriptorLimit` is configured.
- Stabilized local runtime by binding `IOrchestratorStore` back to in-process `OrchestratorStore` for ControlPlane consumers.
- Deferred `RecoveryService` startup recovery pass until `ApplicationStarted` to avoid pre-listen startup races.
- Added MessagePack annotations to `ControlPlane` store-gateway invocation contracts and removed `CancellationToken` from the MagicOnion method signature for serialization compatibility.
- Added the standard LAN-ready `dotnet watch` command for ControlPlane (`0.0.0.0:5266`, polling watcher, explicit reload host).
- ControlPlane now auto-builds `WorkerGateway` image using the local `docker` CLI (`docker build`) when a configured image is missing, then retries container create; if build fails it falls back to image pull.
- Environment overrides for this behavior: `WORKER_GATEWAY_DOCKERFILE_PATH` and `WORKER_GATEWAY_BUILD_CONTEXT`.
- ControlPlane now performs worker image availability checks at startup through `WorkerImageBootstrapService`.
- `WorkerImageBootstrapService` now rethrows bootstrap failures so host startup does not continue in a degraded state.
- WorkerGateway `ImagePrePullService` now attempts local image build for known harness images (`ai-harness`, `ai-harness-base`, `harness-*`) before pull fallback.
- Global `/settings/tools` was removed; tool visibility is now only available in worker detail under `/settings/workers/{workerId}`.
- Worker details now expose per-worker tool availability and version diagnostics for `codex`, `opencode`, `claude-code`, and `zai` with live refresh.
- Added global run-completion audio cues in `MainLayout` via JS interop (`agentsDashboard.playRunCompletedSound`) for terminal states `Succeeded`, `Failed`, and `Cancelled`, with dedicated modern tones per state.
- Added `/settings/sounds` with run-end audio preferences (enabled, volume, selected profile, and per-state toggles), localStorage-backed persistence via JS interop, and a dedicated test button.
- Added local Mixkit sound assets for run completion/error cues and defaulted run-end sounds to a new `mixkit` profile using:
  - `mixkit-message-pop-alert-2354.mp3` for succeeded
  - `mixkit-digital-quick-tone-2866.mp3`, `mixkit-double-beep-tone-alert-2868.mp3`, `mixkit-elevator-tone-2863.mp3` for failed.
