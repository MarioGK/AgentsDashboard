# AI Orchestrator - Development Context

Production-focused, self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution is via CLI harnesses (`codex`, `opencode`, `claude-code`, `zai`).

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
```

## Snapshot

- `src/AgentsDashboard.slnx`: 3 production projects (`AgentsDashboard.ControlPlane`, `AgentsDashboard.WorkerGateway`, `AgentsDashboard.Contracts`).
- Active harness adapters: `CodexAdapter`, `OpenCodeAdapter`, `ClaudeCodeAdapter`, `ZaiAdapter`.
- Blazor components: 40 `.razor` files under `src/AgentsDashboard.ControlPlane/Components`.

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
- Health: `curl http://localhost:5266/health`, `curl http://localhost:5266/ready`, and `curl http://localhost:5266/alive`
- LAN accessibility is mandatory: when started, services must bind to `0.0.0.0` (or equivalent all-interfaces binding), not localhost-only.

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
- Worker pool is elastic (0..N) using `Orchestrator:Workers:*` settings for capacity, startup timeout, idle scale-down, and connectivity mode.
- Terminal bridge is worker-aware: ControlPlane opens/reuses MagicOnion terminal hubs per worker and replays session output from terminal audit events on client reattach.
- Provider secrets are injected into run environment variables and the secrets map; Codex credentials map to both `CODEX_API_KEY` and `OPENAI_API_KEY`.
- If repository Codex secrets are missing, ControlPlane attempts host credential discovery from `OPENAI_API_KEY`/`CODEX_API_KEY` environment variables, then `CODEX_HOME/auth.json` or `~/.codex/auth.json`.
- Standard JSON result envelope with normalized fallback parsing.
- Redact secrets from output.
- Standalone terminal sessions inherit provider API key environment variables from the worker process (`CODEX_API_KEY` is mirrored to `OPENAI_API_KEY` when needed).
- Artifact storage path is configurable via `Orchestrator:ArtifactsRootPath` (default `/data/artifacts`; development override `data/artifacts`).
- Health endpoints use split probes: `/alive` (liveness), `/ready` (dependency readiness), and `/health` (readiness alias with detailed JSON payload).

## Container Notes

- Service Dockerfiles (`ControlPlane`, `WorkerGateway`) must copy root build metadata files (`global.json`, `Directory.Build.props`, `Directory.Packages.props`) before `dotnet restore`.
- `ControlPlane` runtime image includes `curl` for compose health checks and pre-creates `/data`, `/artifacts`, `/workspaces`.

## Last Verified

- Date: 2026-02-16
- Stabilized local runtime by binding `IOrchestratorStore` back to in-process `OrchestratorStore` for ControlPlane consumers.
- Deferred `RecoveryService` startup recovery pass until `ApplicationStarted` to avoid pre-listen startup races.
- Added MessagePack annotations to `ControlPlane` store-gateway invocation contracts for future MagicOnion serialization compatibility.
