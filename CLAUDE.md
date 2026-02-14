# AI Orchestrator - Development Context

## Project Overview

Production-focused, self-hosted AI orchestration platform on .NET 10 where Blazor Server is the control plane and execution is done through CLI harnesses: codex, opencode, claude code, and zai.

## Solution Structure

```
src/
├── AgentsDashboard.ControlPlane/   # Blazor Server UI, REST API, Scheduler, SignalR, YARP
├── AgentsDashboard.WorkerGateway/  # gRPC worker, job queue, harness execution
├── AgentsDashboard.Contracts/      # Shared domain models, API DTOs, gRPC proto
├── AgentsDashboard.ServiceDefaults/# OpenTelemetry, service discovery, health checks
└── AgentsDashboard.AppHost/        # Aspire composition for local dev

tests/
├── AgentsDashboard.UnitTests/      # Unit tests for core services
├── AgentsDashboard.IntegrationTests/   # Testcontainers-based integration tests
└── AgentsDashboard.PlaywrightTests/    # E2E UI tests

deploy/
├── docker-compose.yml              # Full stack deployment
├── harness-image/Dockerfile        # All-in-one harness execution image
└── harness-images/                 # Individual harness Dockerfiles
    ├── Dockerfile.harness-base     # Base image with common dependencies
    ├── Dockerfile.harness-codex    # Codex/GPT harness
    ├── Dockerfile.harness-opencode # OpenCode harness
    ├── Dockerfile.harness-claudecode # Claude Code harness
    ├── Dockerfile.harness-zai      # Zai/GLM-5 harness
    └── build-harness-images.sh     # Build script for all images
```

## Tech Stack

- **.NET 10** with ASP.NET Core
- **Blazor Server** with MudBlazor 8.x UI and BlazorMonaco 3.x editors
- **MongoDB 8.0** as system of record
- **gRPC** for control-plane <-> worker communication
- **YARP 2.x** for reverse proxy (dynamic routes for runs)
- **SignalR** for real-time run status/log updates
- **Aspire** for local orchestration and OpenTelemetry
- **VictoriaMetrics + VMUI** for metrics/observability
- **Docker** for isolated harness execution
- **Docker.DotNet** for image builder service
- **SharpZipLib** for TAR archive creation
- **CliWrap** for CLI process execution in harness adapters

## Product Model

1. **Project** - Umbrella workspace containing repositories
2. **Repository** - Operational unit for tasks, schedules, credentials, findings
3. **Task** - Runnable object (OneShot, Cron, EventDriven)
4. **Run** - Execution instance with logs, artifacts, status
5. **Finding** - Triage item from failed runs, QA issues, approvals

## Key Conventions

### Code Style
- File-scoped namespaces
- Primary constructors where appropriate
- Required properties for DTOs
- No comments unless explicitly requested
- Async suffix on async methods

### Authentication
- Cookie-based auth with roles: viewer, operator, admin
- Policies enforced on API endpoints and Blazor pages

### API Design
- REST endpoints under `/api/`
- No versioning prefix (v1)
- Use domain documents directly (DTOs planned)

### Database
- MongoDB with typed collections
- TTL indexes for logs (30d) and runs (90d)
- Encrypted secrets via Data Protection API

### Execution Model
- Harness-only (no direct provider APIs)
- Worker launches ephemeral Docker containers
- Standardized JSON envelope for results
- Secret redaction on all output
- Artifact extraction from container workspaces

## Running Locally

1. Start infrastructure:
```bash
docker compose -f deploy/docker-compose.yml up -d mongodb victoria-metrics vmui
```

2. Run via Aspire:
```bash
dotnet run --project src/AgentsDashboard.AppHost
```

Or individually:
```bash
dotnet run --project src/AgentsDashboard.WorkerGateway
dotnet run --project src/AgentsDashboard.ControlPlane
```

3. Access:
- Dashboard: http://localhost:5266
- VMUI: http://localhost:8081

## Building Harness Images

```bash
# Build all harness images
./deploy/harness-images/build-harness-images.sh [registry] [tag]

# Or build individually
docker build -f deploy/harness-images/Dockerfile.harness-base -t ai-harness-base:latest .
docker build -f deploy/harness-images/Dockerfile.harness-codex -t harness-codex:latest .
docker build -f deploy/harness-images/Dockerfile.harness-opencode -t harness-opencode:latest .
docker build -f deploy/harness-images/Dockerfile.harness-claudecode -t harness-claudecode:latest .
docker build -f deploy/harness-images/Dockerfile.harness-zai -t harness-zai:latest .
```

## Testing

```bash
# Unit tests
dotnet test tests/AgentsDashboard.UnitTests

# Integration tests (requires Docker)
dotnet test tests/AgentsDashboard.IntegrationTests

# Playwright E2E tests (requires app running on localhost:8080)
dotnet test tests/AgentsDashboard.PlaywrightTests

# All tests
dotnet test
```

## Test Coverage Summary

| Test Project | Files | Tests | Coverage Area |
|--------------|-------|-------|---------------|
| UnitTests | 27+ | ~612 | Alerting, Cron, Dispatcher, Templates, gRPC, Adapters, Executor, Queue, Redactor, Workflow, Proxy, Recovery, CredentialValidation, HarnessHealth, ArtifactExtractor, DockerContainer, JobProcessor, Heartbeat, HealthCheck, EventListener, EventPublisher |
| IntegrationTests | 28+ | ~152 | MongoDB store, Image allowlist, Secret redactor, API endpoints |
| PlaywrightTests | 10 | ~183 | Dashboard, Workflows, ImageBuilder, Alerts, Findings, Runs, Tasks, Repos, Settings |

## Implementation Status

| Feature | Status | Notes |
|---------|--------|-------|
| Project/Repository/Task hierarchy | Complete | Full CRUD + UI, task editing, delete support |
| Run lifecycle with concurrency | Complete | Create, start, cancel, retry, approve |
| Findings inbox | Complete | Filter, assign, acknowledge, retry, create follow-up task |
| Scheduler (cron) | Complete | Cronos library, 10s tick |
| Webhooks (event-driven) | Complete | Token auth, EventDriven tasks |
| SignalR real-time updates | Complete | Status updates + live log streaming |
| YARP dynamic proxy | Complete | TTL cleanup, audit middleware |
| Secret encryption | Complete | Data Protection API |
| Harness adapters | Complete | Codex, OpenCode, ClaudeCode, Zai (GLM-5) with adapter interface |
| Harness execution | Complete | Docker isolation, JSON envelopes, sandbox profiles |
| Sandbox profiles | Complete | CPU/memory limits, network isolation, read-only fs |
| Volume mounts | Complete | Repository cloning and workspace mounting |
| Worker heartbeat | Complete | gRPC Heartbeat RPC, slot tracking |
| Built-in task templates | Complete | QA Browser Sweep, Unit Test Guard, Dependency Health, Regression Replay |
| Workflows | Complete | Model, API, persistence, executor + visual editor |
| Provider settings | Complete | Per-repo secrets for all harnesses + GitHub |
| Image builder service | Complete | Build, list, delete via API + Monaco editor |
| Approval workflow | Complete | PendingApproval state, approve/reject |
| Artifact storage | Complete | File-based, download via API |
| Artifact extraction | Complete | Automatic extraction from container workspaces |
| Alerting service | Complete | 5 rule types, webhook firing |
| Alert rules UI | Complete | Create/edit/delete rules, event timeline |
| Worker management UI | Complete | Worker list with status, slots, utilization |
| CRUD operations | Complete | PUT/DELETE for tasks, repos, projects |
| CI/CD pipeline | Complete | GitHub Actions workflow with coverage |
| Unit tests | Complete | ~612 tests for core services |
| Integration tests | Complete | 152 tests: store, allowlist, redactor, API endpoints |
| E2E tests | Complete | 183 Playwright tests across 10 test files |
| Global project/repo switcher | Complete | MudSelect dropdowns in MainLayout header |
| Aggregate reliability metrics | Complete | Success rates (7d/30d), avg duration, failure trends on Dashboard |
| Stage timeline visualization | Complete | MudTimeline in RunDetail Workflow tab with color-coded stages, duration, expandable runs |
| Harness health indicators | Complete | HarnessHealthService + status chips in MainLayout |
| Credential validation | Complete | Test connection button on ProviderSettings, validates GitHub/Anthropic/OpenAI/Z.ai |
| System Settings UI | Complete | /settings page with Docker policy, retention, observability |
| Repository instruction files | Complete | Repo-level instruction files with BlazorMonaco editor, layered prompts |
| Harness Docker images | Complete | Individual images for each harness + all-in-one image |

## UI Pages Summary

| Page | Route | Status |
|------|-------|--------|
| Dashboard | `/` | Complete |
| Projects | `/projects` | Complete |
| Project Detail | `/projects/{id}` | Complete |
| Repository Detail | `/repositories/{id}` | Complete (tasks, runs, findings, secrets) |
| Run Kanban | `/runs` | Complete (SignalR real-time) |
| Run Detail | `/runs/{id}` | Complete (logs, artifacts, proxy, approve/reject) |
| Findings List | `/findings` | Complete (filters, actions) |
| Finding Detail | `/findings/{id}` | Complete (assign, retry, create task) |
| Schedules | `/schedules` | Complete |
| Workers | `/workers` | Complete |
| Workflows | `/workflows` | Complete |
| Workflow Editor | `/workflows/{id}` | Complete |
| Image Builder | `/image-builder` | Complete |
| Provider Settings | `/providers` | Complete |
| Alert Settings | `/alerts` | Complete |
| System Settings | `/settings` | Complete (Docker policy, retention, observability) |
| Login | `/login` | Complete |

## Missing Test Coverage

- None (bunit component tests added)

## Component Tests (bunit)

Located in `tests/AgentsDashboard.UnitTests/ControlPlane/Components/`:
- `DashboardTests.cs` - 6 tests for Overview page
- `RunKanbanTests.cs` - 8 tests for Runs Kanban board
- `FindingsListTests.cs` - 8 tests for Findings list page

Run component tests:
```bash
dotnet test tests/AgentsDashboard.UnitTests --filter "FullyQualifiedName~Components"
```

## Build Commands

```bash
dotnet build src/AgentsDashboard.slnx
dotnet test
dotnet format
```
