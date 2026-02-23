# AgentsDashboard

Self-hosted AI orchestration platform on `.NET 10`.

Control plane UI and orchestration run in Blazor Server; execution runs in task runtime workers through MagicOnion and harness CLIs (`codex`, `opencode`).

## Architecture

- `src/AgentsDashboard.ControlPlane`
  - Blazor Server UI (Workspace, Overview, Search, Settings)
  - orchestration/scheduling/dispatch
  - MagicOnion server + client integrations
  - LiteDB-backed persistence and projections
- `src/AgentsDashboard.TaskRuntime`
  - MagicOnion runtime service
  - queued harness execution
  - codex app-server runtime + opencode SSE runtime
- `src/AgentsDashboard.Contracts`
  - shared domain models
  - MagicOnion contracts + runtime message contracts

All internal service-to-service communication is MagicOnion-based.

## Core Features

- Repository -> Task -> Run orchestration model
- Task kinds: `OneShot`, `EventDriven`
- Harnesses: `codex`, `opencode`
- Run modes: `Default`, `Plan`, `Review`
- Runtime lifecycle tracking (`Ready`, `Busy`, `Inactive`, `Failed`, etc.)
- Structured run event ingestion and projections:
  - `RunStructuredEventDocument`
  - `RunDiffSnapshotDocument`
  - `RunToolProjectionDocument`
- Alerts, session profiles, skills, task runtimes, MCP settings
- Semantic chunk indexing/search with LiteDB-backed storage and in-process cosine fallback

## Persistence

- LiteDB-only persistence (`LiteDB 6.0.0-prerelease.75`)
- Repository access through `IRepository<>` abstractions
- ControlPlane orchestration data split by domain store interfaces:
  - `IRepositoryStore`, `ITaskStore`, `IRunStore`, `IRuntimeStore`, `ISystemStore`
- DB access serialized through the LiteDB execution path
- Run artifacts and workspace image payloads stored in LiteDB file storage
- Workspace image uploads are compressed to lossless WebP before persistence

Runtime data paths resolve under repo-local `data/` by default.

## Repository Layout

```text
src/
  AgentsDashboard.ControlPlane
  AgentsDashboard.TaskRuntime
  AgentsDashboard.Contracts
  AgentsDashboard.slnx

tests/
  AgentsDashboard.UnitTests
  AgentsDashboard.IntegrationTests
  AgentsDashboard.Playwright

deploy/
  harness-image/
```

## Prerequisites

- .NET SDK `10.0.3`
- Docker (required for runtime container lifecycle in normal local orchestration)
- Node.js 20+ (Playwright suite)
- Local mkcert root CA for trusted HTTPS (see `docs/INSTALL_CA_CLIENT.md`)

## Local Run

### Recommended dev loop (LAN-friendly watch)

```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=true \
DOTNET_USE_POLLING_FILE_WATCHER=1 \
DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 \
ASPNETCORE_ENVIRONMENT=Development \
dotnet watch --project src/AgentsDashboard.ControlPlane
```

- Replace `192.168.10.101` with your machine LAN IP when needed.
- App URL (example LAN host): `https://192.168.10.101:5266`
- Health endpoints:
  - `/alive` (liveness)
  - `/ready` (readiness)
  - `/health` (readiness alias + JSON payload)

### Direct run with launch profile defaults

```bash
dotnet run --project src/AgentsDashboard.ControlPlane
```

Default launch profile URL is `https://0.0.0.0:5266`.
- ControlPlane is configured to use `/home/mariogk/.local/share/mkcert/terrascale-dev.pem` when available, matching the TerraScale dev cert.

### Docker Compose

```bash
docker compose up -d
```

- Control plane URL: `http://localhost:8080`
- Compose mounts `/var/run/docker.sock` and `./data:/app/data`

### Run TaskRuntime directly (optional)

```bash
dotnet run --project src/AgentsDashboard.TaskRuntime
```

TaskRuntime listens on `http://0.0.0.0:5201` (HTTP/2).

## Build, Test, Format

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# All tests
dotnet test --solution src/AgentsDashboard.slnx

# Focused tests (MTP/TUnit filter examples)
dotnet test --project tests/AgentsDashboard.UnitTests/AgentsDashboard.UnitTests.csproj -- --treenode-filter "/*RunDispatcher*"
dotnet test --project tests/AgentsDashboard.UnitTests/AgentsDashboard.UnitTests.csproj -- --filter-uid "<uid>"

# Format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error
```

If local build behavior gets inconsistent:

```bash
dotnet build-server shutdown
```

## Playwright E2E

```bash
cd tests/AgentsDashboard.Playwright
npm install
npm run install:browsers
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_REPO_REMOTE_PATH=/abs/path/to/seeded-remote.git \
PLAYWRIGHT_E2E_REPO_CLONE_ROOT=/abs/path/to/clones \
npm test
```

## Execution Notes

- ControlPlane dispatches runs to TaskRuntime via `ITaskRuntimeService.DispatchJobAsync`.
- Task prompts include required git start/end steps and keep work on the repository default branch.
- Successful runs that produce no effective diff can be marked `Obsolete` by runtime disposition metadata.

## Additional Docs

- `docs/HTTPS.md`
- `docs/INSTALL_CA_CLIENT.md`
- `docs/ai/codex-server.md`
- `docs/ai/opencode-server.md`
- `docs/logging.md`
