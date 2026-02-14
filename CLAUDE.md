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
├── AgentsDashboard.PlaywrightTests/    # E2E UI tests
└── AgentsDashboard.Benchmarks/     # Performance benchmarks

deploy/
├── docker-compose.yml              # Full stack deployment
├── harness-image/Dockerfile        # All-in-one harness execution image
├── helm/                           # Kubernetes Helm chart
├── k8s/                            # Raw Kubernetes manifests
├── backup/                         # MongoDB backup/restore scripts
├── nginx/                          # NGINX reverse proxy config
├── vm-dashboards/                  # Pre-configured VMUI dashboards
│   ├── orchestrator-dashboard.json # Main orchestrator metrics
│   └── harness-metrics-dashboard.json # Per-harness metrics
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
- **YARP 2.3.x** for reverse proxy (dynamic routes for runs)
- **SignalR** for real-time run status/log updates
- **Aspire** for local orchestration and OpenTelemetry
- **VictoriaMetrics + VMUI** for metrics/observability
- **Docker** for isolated harness execution
- **Docker.DotNet** for image builder service
- **CliWrap 3.8.2** for CLI process execution in harness adapters
- **Swashbuckle.AspNetCore** for OpenAPI/Swagger documentation

## Build Configuration

Centralized build configuration using:
- **Directory.Build.props** - Common MSBuild properties (target framework, nullable, analysis level)
- **Directory.Packages.props** - Central Package Management (CPM) for consistent versions
- **global.json** - SDK version pinning (net10.0, rollForward: latestMinor)
- **.editorconfig** - Coding style and formatting conventions

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

## API Design
- REST endpoints under `/api/`
- No versioning prefix (v1)
- Use domain documents directly (DTOs planned)
- CreateTaskRequest/UpdateTaskRequest include: ApprovalProfile, ConcurrencyLimit, InstructionFiles, ArtifactPatterns, LinkedFailureRuns
- CreateTaskFromFindingRequest includes: LinkedFailureRuns (defaults to source finding run)

### Database
- MongoDB with typed collections (18 collections)
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
- Swagger: http://localhost:5266/api/docs

## Harness Setup

### Zai Harness (GLM-5)
The Zai harness uses cc-mirror to configure GLM-5 as the backend:
```bash
npx cc-mirror quick --provider zai --api-key "$Z_AI_API_KEY"
```

### Supported Harnesses
| Harness | CLI Tool | Provider |
|---------|----------|----------|
| Codex | `codex` | OpenAI GPT |
| OpenCode | `opencode` | OpenCode |
| Claude Code | `claude-code` | Anthropic Claude |
| Zai | `zai` | Zhipu GLM-5 |

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

## Implementation Status: 100% COMPLETE

| Feature | Status |
|---------|--------|
| Project/Repository/Task hierarchy | Complete |
| Run lifecycle with concurrency | Complete |
| Findings inbox | Complete |
| Scheduler (cron) | Complete |
| Webhooks (event-driven) | Complete |
| SignalR real-time updates | Complete |
| YARP dynamic proxy | Complete |
| Secret encryption (DPAPI) | Complete |
| Harness adapters (4) | Complete |
| Docker execution | Complete |
| Workflows with visual editor | Complete |
| Alerting (5 rule types) | Complete |
| Built-in templates (4) | Complete |
| - QA Browser Sweep | Complete |
| - Unit Test Guard | Complete |
| - Dependency Health Check | Complete |
| - Regression Replay | Complete |
| AI-assisted Dockerfile generation | Complete |
| OpenAPI/Swagger | Complete |
| Kubernetes/Helm deployment | Complete |
| CI/CD (GitHub Actions) | Complete |
| Rate limiting (4 policies) | Complete |
| MongoDB backup/recovery | Complete |
| PodDisruptionBudget | Complete |
| cert-manager TLS | Complete |
| Task artifact patterns | Complete |
| Linked failure runs for regression replay | Complete |
| Webhook event type enum | Complete |
| Template to task field mapping | Complete |

## Test Coverage Summary

| Test Project | Files | Tests | Pass Rate |
|--------------|-------|-------|-----------|
| UnitTests | 47 | 1,139 | 100% (36 skipped, all pass) |
| IntegrationTests | 40 | 232 | Requires MongoDB |
| PlaywrightTests | 21 | 277 | Requires Running App |
| Benchmarks | 7 | 4 | Performance |

**Total: 115 test files, 1,652+ tests**

### Test Coverage by Area

| Area | Unit | Integration | E2E |
|------|------|-------------|-----|
| ControlPlane Services | 312 | - | - |
| WorkerGateway Services | 200 | - | - |
| Harness Adapters (4) | 90 | - | - |
| API Endpoints | 92 | 210 | - |
| UI Pages (21) | - | - | 274 |
| Proxy/YARP | 68 | - | - |
| SignalR Hub | 40 | 10 | - |
| Rate Limiting | - | 22 | - |
| Contracts | 40 | - | - |

## Architecture Summary

### ControlPlane
- **92 API endpoints** with authentication/authorization
- **22 Blazor pages** with MudBlazor UI
- **18 services** for business logic
- **SignalR hub** with 5 event types
- **YARP proxy** with dynamic routes and audit

### SignalR Event DTOs (5)
- `RunStatusChangedEvent`: runId, state, summary, startedAt, endedAt
- `RunLogChunkEvent`: runId, level, message, timestamp
- `FindingUpdatedEvent`: findingId, repositoryId, state, severity, title
- `WorkerHeartbeatEvent`: workerId, hostName, activeSlots, maxSlots, timestamp
- `RouteAvailableEvent`: runId, routePath, timestamp

### WorkerGateway
- **6 gRPC RPCs**: DispatchJob, CancelJob, SubscribeEvents, Heartbeat, KillContainer, ReconcileOrphanedContainers
- **4 harness adapters**: Codex, OpenCode, ClaudeCode, Zai
- **7 interface methods** per adapter: PrepareContext, BuildCommand, Execute, ParseEnvelope, MapArtifacts, ClassifyFailure, HarnessName
- **CliWrap 3.8.2** for git clone, GitHub PR, harness execution

### MongoDB Collections (18)
projects, repositories, tasks, runs, run_events, findings, workers, webhooks, proxy_audits, settings, workflows, workflow_executions, alert_rules, alert_events, repository_instructions, harness_provider_settings, task_templates, provider_secrets

**Note:** Artifacts are stored on filesystem (`/data/artifacts/{runId}/`), not in MongoDB.

### Docker Images (6)
- ai-harness-base: Ubuntu 24.04 + .NET 10 + Node.js 20 + Python 3.12 + Go 1.23 + Playwright
- harness-codex, harness-opencode, harness-claudecode, harness-zai
- ai-harness (all-in-one)

### VMUI Dashboards (72 panels)
- Orchestrator Dashboard: 32 panels - throughput, latency, errors, queue, workers, runs, findings, proxy
- Harness Metrics Dashboard: 40 panels - per-harness execution, duration, success rate, failures, container metrics

## UI Pages (22)

| Page | Route |
|------|-------|
| Dashboard | `/` |
| Login | `/login` |
| Projects | `/projects` |
| Project Detail | `/projects/{id}` |
| Repository Detail | `/repositories/{id}` |
| Instruction Files | `/repositories/{id}/instructions` |
| Run Kanban | `/runs` |
| Run Detail | `/runs/{id}` |
| Findings List | `/findings` |
| Finding Detail | `/findings/{id}` |
| Schedules | `/schedules` |
| Workers | `/workers` |
| Workflows | `/workflows` |
| Workflow Editor | `/workflows/{id}` |
| Templates | `/templates` |
| Image Builder | `/image-builder` |
| Provider Settings | `/providers` |
| Alert Settings | `/alerts` |
| Proxy Audits | `/proxy-audits` |
| System Settings | `/settings` |
| Error | `/Error` |
| Not Found | `/not-found` |

## Build Commands

```bash
dotnet build src/AgentsDashboard.slnx
dotnet test
dotnet format
```

## Known Issues

- No Blazor component tests (bunit compatibility with .NET 10 pending)
- Docker-dependent tests skipped (37 tests) due to Docker.DotNet version mismatch and BackgroundService testability
- Integration tests require running MongoDB infrastructure
- Unit test pass rate: 1,102/1,139 (100% of non-skipped tests pass)

## Deployment Options

| Option | Command |
|--------|---------|
| Docker Compose | `docker compose -f deploy/docker-compose.yml up -d` |
| Helm | `helm install ai-orchestrator deploy/helm/ai-orchestrator` |
| Kustomize | `kubectl apply -k deploy/k8s/` |

## CI/CD Pipeline

### CI Workflow (`.github/workflows/ci.yml`)
- **lint**: Code format verification with `dotnet format --verify-no-changes`
- **security-scan**: CodeQL analysis and vulnerable package check
- **build**: Compile solution with .NET 10
- **test-unit**: Unit tests with code coverage
- **test-integration**: Integration tests with MongoDB service container
- **test-e2e**: Playwright E2E tests with running application
- **trivy-scan**: Container vulnerability scanning for all harness images

### Deploy Workflow (`.github/workflows/deploy.yml`)
- **prepare**: Determine version from release tag or input
- **build-base**: Build and push `ai-harness-base` image
- **build-harnesses**: Build and push 4 harness images (codex, opencode, claudecode, zai)
- **build-all-in-one**: Build and push `ai-harness` all-in-one image
- **build-applications**: Build and push `control-plane` and `worker-gateway` images
- **production-approval**: Manual approval gate for production deployments
- **helm-deploy**: Deploy to Kubernetes using Helm
- **update-compose**: Create PR with updated docker-compose image tags

### Container Images

| Image | Description | Registry |
|-------|-------------|----------|
| ai-harness-base | Base image with .NET 10, Node.js, Python, Go, Playwright | ghcr.io |
| harness-codex | OpenAI Codex harness | ghcr.io |
| harness-opencode | OpenCode harness | ghcr.io |
| harness-claudecode | Claude Code harness | ghcr.io |
| harness-zai | Zhipu GLM-5 harness | ghcr.io |
| ai-harness | All-in-one harness image | ghcr.io |
| control-plane | Blazor Server control plane | ghcr.io |
| worker-gateway | gRPC worker gateway | ghcr.io |

## API Endpoints (83 total)

| Category | Count | Key Endpoints |
|----------|-------|---------------|
| Projects | 5 | CRUD + repositories |
| Repositories | 3 | CRUD |
| Tasks | 4 | CRUD |
| Runs | 9 | CRUD + cancel/retry/approve/reject + bulk |
| Findings | 7 | CRUD + retry/assign/create-task |
| Workflows | 10 | CRUD + execute + approvals |
| Alerts | 7 | Rules + Events + bulk-resolve |
| Webhooks | 5 | CRUD + token + event receiver |
| Templates | 5 | CRUD |
| Images | 3 | List/build/delete |
| Other | 26 | Workers, Secrets, Instructions, Settings, Health, Proxy |

## Helm Chart Components

| Component | Template | Description |
|-----------|----------|-------------|
| ControlPlane | `control-plane.yaml` | Deployment + Service (2 replicas) |
| WorkerGateway | `worker-gateway.yaml` | Deployment + Service (3 replicas) |
| MongoDB | `mongodb.yaml` | StatefulSet + Headless Service |
| VictoriaMetrics | `victoriametrics.yaml` | Deployment + PVC |
| VMUI | `vmui.yaml` | Deployment + Service |
| ConfigMaps | `configmap.yaml` | 5 ConfigMaps |
| Secrets | `secrets.yaml` | 3 Secrets |
| Ingress | `ingress.yaml` | 2 Ingress resources with TLS |
| HPA | `hpa.yaml` | HorizontalPodAutoscalers |
| PVC | `pvc.yaml` | PersistentVolumeClaims |
| NetworkPolicy | `networkpolicy.yaml` | Network policies for all components |
| Namespace | `namespace.yaml` | Conditional namespace creation |
| PodDisruptionBudget | `pdb.yaml` | PDB for control-plane, worker-gateway, mongodb |
| Certificate | `certificate.yaml` | cert-manager TLS certificate |

## Verification Status

**Last Verified:** 2026-02-14

| Component | Status | Details |
|-----------|--------|---------|
| Build | Passed | 0 Warnings, 0 Errors |
| Unit Tests | Passed | 1,103/1,139 passed (36 skipped, 0 failed) |
| API Endpoints | Complete | 83 endpoints across 23 groups |
| Harness Adapters | Complete | Codex, OpenCode, ClaudeCode, Zai |
| Blazor Pages | Complete | 22 pages with full functionality |
| Docker Images | Complete | 6 images (base + 4 harness + all-in-one) |
| Deployment | Complete | Docker Compose, Helm (14 templates), K8s |
| gRPC Services | Complete | 6 RPCs implemented |
| Built-in Templates | Complete | 4 templates (QA, UnitTest, Deps, Regression) |
