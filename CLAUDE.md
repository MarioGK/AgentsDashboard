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

## API Design
- REST endpoints under `/api/`
- No versioning prefix (v1)
- Use domain documents directly (DTOs planned)
- CreateTaskRequest/UpdateTaskRequest include: ApprovalProfile, ConcurrencyLimit, InstructionFiles

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
| UnitTests | 41 | 1083 | Alerting, Cron, Templates, gRPC, Adapters, Executor, Queue, Redactor, Workflow, Proxy, Recovery, CredentialValidation, HarnessHealth, ArtifactExtractor, DockerContainer, JobProcessor, Heartbeat, HealthCheck, EventListener, EventPublisher, GlobalSelection, Envelope Validation, Dead-run Detection, Container Reaping, WorkerEventBus, ImagePrePull, ContainerOrphanReconciler |
| IntegrationTests | 29 | 166 | MongoDB store, Image allowlist, Secret redactor, API endpoints, Concurrency stress, Performance |
| PlaywrightTests | 12 | 213 | Dashboard, Workflows, ImageBuilder, Alerts, Findings, Runs, Tasks, Repos, Settings |
| Benchmarks | 4 | - | WorkerQueue, SignalR Publish, MongoDB Operations |

**Total: 86 test files, 1462 tests**

### Test Notes

- **RecoveryServiceTests**: Updated to use IContainerReaper interface instead of direct WorkerGatewayClient
- **ContainerReaperTests**: Tests for container reaping via gRPC
- **WorkerEventBusTests**: Tests for event bus channel operations
- **DeadRunDetectionTests**: Tests run successfully using mocks (no timer required)
- **ImagePrePullServiceTests**: Tests for image pre-pull service
- **ContainerOrphanReconcilerTests**: Tests for orphaned container reconciliation

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
| Alert cooldown mechanism | Complete | 15-minute default cooldown, configurable per rule |
| Alert resolution API | Complete | POST /alerts/events/{id}/resolve endpoint |
| Workflow execution recovery | Complete | Orphaned workflows marked as failed on startup |
| Pending approval logging | Complete | Logs pending approval runs on startup |
| Alert rules UI | Complete | Create/edit/delete rules, event timeline |
| Worker management UI | Complete | Worker list with status, slots, utilization |
| CRUD operations | Complete | PUT/DELETE for tasks, repos, projects |
| CI/CD pipeline | Complete | GitHub Actions workflow with coverage |
| Unit tests | Complete | ~850 tests for core services |
| Integration tests | Complete | ~160 tests: store, allowlist, redactor, API endpoints |
| E2E tests | Complete | 210 Playwright tests across 11 test files |
| Global project/repo switcher | Complete | MudSelect dropdowns in MainLayout header |
| Aggregate reliability metrics | Complete | Success rates (7d/30d), avg duration, failure trends on Dashboard |
| Stage timeline visualization | Complete | MudTimeline in RunDetail Workflow tab with color-coded stages, duration, expandable runs |
| Harness health indicators | Complete | HarnessHealthService + status chips in MainLayout |
| Credential validation | Complete | Test connection button on ProviderSettings, validates GitHub/Anthropic/OpenAI/Z.ai |
| System Settings UI | Complete | /settings page with Docker policy, retention, observability |
| Repository instruction files | Complete | Repo-level instruction files with BlazorMonaco editor, layered prompts |
| Harness Docker images | Complete | Individual images for each harness + all-in-one image |
| Container security hardening | Complete | no-new-privileges, cap-drop ALL, agent user |
| Artifact volume mount | Complete | /artifacts mount for container-to-host transfer |
| Container metrics | Complete | CPU/memory/network/block I/O collection |
| Image pre-pull | Complete | Pre-pulls harness images on startup |
| Dead-run protection | Complete | Stale/zombie/overdue run detection, forced container termination |
| Harness envelope validation | Complete | JSON schema validation for standardized harness output |
| Branch naming convention | Complete | Enforces agent/<repo>/<task>/<runId> format for PR branches |
| Orphan container reconciliation | Complete | Detects and removes containers without matching runs on startup |
| Performance tests | Complete | 50 concurrent jobs, sub-2s p95 latency stress tests |
| VMUI dashboards | Complete | Orchestrator + harness-specific metrics dashboards |
| Per-stage timeout | Complete | Configurable timeouts per workflow stage with max caps |
| Docker Compose hardening | Complete | Healthchecks, resource limits, network isolation, artifact persistence |

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
| Proxy Audits | `/proxy-audits` | Complete |
| System Settings | `/settings` | Complete (Docker policy, retention, observability) |
| Login | `/login` | Complete |

## Missing Test Coverage

- No Blazor component tests currently (bunit compatibility with .NET 10 pending)
- Some sealed classes (HarnessExecutor, JobProcessorService) cannot be mocked with Moq

## Known Issues

- ~75 unit tests fail due to sealed classes (HarnessExecutor, JobProcessorService) and IJSRuntime extension methods that cannot be mocked with Moq
- No Blazor component tests currently (bunit compatibility with .NET 10 pending)
- Integration tests require running MongoDB and Docker infrastructure
- Some GlobalSelectionService/ProjectContext tests fail due to IJSRuntime extension method mocking limitations

## Recent Fixes (2026-02-14)

### Test Improvements
- Added `virtual` keyword to all public Task-returning methods in OrchestratorStore.cs for Moq compatibility
- Fixed WorkerGatewayGrpcServiceTests to use IDockerContainerService interface instead of sealed class
- Fixed ApiEndpointsTests to properly mock OrchestratorStore with MongoDB mocks
- Restored ContainerMetrics class in WorkerGateway.Models namespace
- Resolved ambiguous type references between WorkerGateway.Models.ContainerMetrics and Contracts.Domain.ContainerMetrics
- Unit test pass rate improved from 914/1057 to 985/1076 (91.5% pass rate)

### Bug Fixes
- Fixed IDockerContainerService.cs: Removed ambiguous ContainerMetrics reference
- Fixed DockerContainerService.cs: Uses Models.ContainerMetrics consistently
- Fixed OrchestratorContainerInfo.cs: Restored ContainerMetrics class definition

- Fixed RunDispatcher.cs: Removed unnecessary FormatMemoryLimit call (MemoryLimit is already a string)
- Fixed TaskTemplateService.cs: Changed memory limit values from long (4294967296L) to string format ("4g", "2g")
- Fixed HarnessAdapterBase.cs: Added ArtifactsHostPath initialization using WorkerOptions.ArtifactStoragePath
- Fixed multiple test files for compilation:
  - Added using statements and type aliases for WorkerGatewayClient
  - Fixed type names: ApprovalProfileConfig, TimeoutConfig, ArtifactPolicyConfig, DispatchJobReply
  - Fixed AsyncUnaryCall constructor parameters
  - Replaced `with` expressions with helper methods for class types

## Additional Improvements (2026-02-14)

### New Models Added
- **HarnessType enum**: Codex, OpenCode, ClaudeCode, Zai - standardized harness type identifiers
- **ContainerMetrics**: CPU percent, memory usage/limit/percent, network RX/TX, block read/write, timestamp
- **ArtifactDocument**: Persistent artifact metadata with run ID, file name, path, content type, size, SHA256, artifact type
- **DeadRunDetectionResult**: Tracks stale/zombie/overdue run termination counts from monitoring cycle

### API Enhancements
- **Alert Rules**: Added `CooldownMinutes` to `CreateAlertRuleRequest` and `UpdateAlertRuleRequest` (default: 15 minutes)
- **Container Stats**: Added `GetContainerStatsAsync` method to `IDockerContainerService` for real-time container metrics

### UI Improvements
- **Projects page**: Added delete button with confirmation dialog for project deletion
- **Project Detail page**: Added delete buttons for project and repositories with confirmation dialogs
- Delete operations use MudBlazor DialogService for user confirmation

### Worker Gateway Improvements
- **Graceful shutdown**: JobProcessorService now tracks running jobs and waits for completion (30s timeout) during shutdown
- **Container metrics**: DockerContainerService implements container stats collection (CPU, memory, network, block I/O)

### Observability
- **gRPC Instrumentation**: Enabled OpenTelemetry gRPC client instrumentation in ServiceDefaults for distributed tracing

### Recovery Service Improvements
- **Testable Detection Methods**: Dead run detection methods now return termination counts and are directly callable for testing
- `DetectAndTerminateStaleRunsAsync()`, `DetectAndTerminateZombieRunsAsync()`, `DetectAndTerminateOverdueRunsAsync()` return int counts
- `MonitorForDeadRunsAsync()` returns `DeadRunDetectionResult` with aggregate counts

### Known Issues Updated
- DockerContainerService now has `IDockerContainerService` interface for mocking in tests
- ContainerMetrics duplicate in WorkerGateway.Models removed (now uses Contracts.Domain.ContainerMetrics)
- DeadRunDetectionTests now pass without requiring timer-based integration tests

### Tests Updated
- **DeadRunDetectionTests**: 10 tests now pass (previously 4 skipped, now all run directly against detection methods)
- **ImagePrePullServiceTests**: 17 tests for image pre-pull service
- **ContainerOrphanReconcilerTests**: 12 tests for orphaned container reconciliation

## Build Commands

```bash
dotnet build src/AgentsDashboard.slnx
dotnet test
dotnet format
```

## Additional Improvements (2026-02-14 - Session 2)

### Bug Fixes
- **Instruction Files Layering**: Fixed `RunDispatcher.BuildLayeredPromptAsync()` to include both:
  - Repository instructions from the separate collection (`repository_instructions`)
  - Embedded instruction files from `RepositoryDocument.InstructionFiles`
  - Task-level instruction files from `TaskDocument.InstructionFiles`
  - Layering order: Repository Collection -> Embedded Repo Instructions -> Task Instructions -> Task Prompt

### Testability Improvements
- **Branch Naming Validation Tests**: Added 9 new tests for `ValidateBranchName` and `BuildExpectedBranchPrefix` methods
- **InternalsVisibleTo**: Added `InternalsVisibleTo` attribute to WorkerGateway project for test project access
- Made `ValidateBranchName` and `BuildExpectedBranchPrefix` methods internal (was private) for unit testing

### Verification Completed
- **Task Templates**: Verified all 4 built-in templates (QA Browser Sweep, Unit Test Guard, Dependency Health Check, Regression Replay) are complete and match plan requirements
- **CliWrap Usage**: Verified CliWrap 3.8.2 is properly used with robust error handling, cancellation support, and secret redaction
- **Branch Naming Convention**: Verified full implementation in RunDispatcher (generation) and HarnessExecutor (validation)
- **Instruction Files**: Fixed bug where embedded repo instruction files were not included in layered prompts

## Additional Improvements (2026-02-14 - Session 3)

### API Enhancements
- **Webhook Management**: Added `GET /api/repositories/{id}/webhooks` endpoint to list webhooks for a repository
- **Webhook Deletion**: Added `DELETE /api/webhooks/{id}` endpoint to delete webhooks
- **Bulk Cancel Runs**: Added `POST /api/runs/bulk-cancel` endpoint for cancelling multiple runs at once
- **Bulk Resolve Alerts**: Added `POST /api/alerts/events/bulk-resolve` endpoint for resolving multiple alert events
- Added `BulkCancelRunsRequest`, `BulkResolveAlertsRequest`, and `BulkOperationResult` DTOs

### Configuration Validation
- **OrchestratorOptions**: Implemented `IValidatableObject` for startup validation
- Validates: connection strings, concurrency limits hierarchy, scheduler interval bounds
- Options registered with `.ValidateOnStart()` for early failure detection

### Docker Improvements
- **All-in-One Image**: Refactored to extend from `ai-harness-base:latest` instead of duplicating base setup
- Reduced image build time and maintenance burden
- Added ripgrep and fd-find utilities

### Test Coverage Updates
- **MongoInitializationService**: 8 unit tests covering initialization, logging, error handling
- **ProjectContext**: 18 unit tests covering project/repo selection, localStorage persistence, edge cases
- Login and Workers E2E tests already exist in DashboardE2ETests.cs

### Store Methods Added
- `DeleteWebhookAsync(webhookId)`: Delete a webhook registration
- `ResolveAlertEventsAsync(eventIds)`: Bulk resolve alert events
- `BulkCancelRunsAsync(runIds)`: Bulk cancel runs by ID

### Documentation Updates
- Updated Missing Test Coverage section: MongoInitializationService and ProjectContext now have dedicated tests
- Removed stale items from Known Issues

## Additional Improvements (2026-02-14 - Session 4)

### Interface Extraction for Testability
- **IOrchestratorStore**: Created interface with all 82 public methods from OrchestratorStore for better Moq compatibility
- **IWorkflowExecutor**: Created interface for WorkflowExecutor with ExecuteWorkflowAsync and ApproveWorkflowStageAsync methods
- Updated all services to use interfaces instead of concrete types:
  - RunDispatcher, WorkflowExecutor, CronSchedulerService, WorkerEventListenerService
  - RecoveryService, AlertingService, TaskTemplateService, WebhookService, GlobalSelectionService, ProjectContext
- Program.cs updated to register interfaces with DI container

### Test Improvements
- Fixed OpenTelemetry.Instrumentation.GrpcNetClient package version (1.14.0 -> 1.15.0-beta.1)
- Updated all test files to use interfaces instead of concrete classes:
  - ApiEndpointsTests, RunDispatcherTests, WebhookServiceTests
  - GlobalSelectionServiceTests, RecoveryServiceTests, DeadRunDetectionTests
- Unit test pass rate improved from 985/1076 to 1017/1105 (92% pass rate)

### Deployment Fixes
- Added grpc_health_probe to WorkerGateway Dockerfile for proper healthcheck support
- Installed wget and ca-certificates for downloading the health probe binary

### Remaining Test Failures (75 tests)
- IJSRuntime extension methods cannot be mocked with Moq (affects GlobalSelectionService, ProjectContext tests)
- Sealed classes (HarnessExecutor, JobProcessorService, DockerContainerService) cannot be mocked with Moq
- These require either creating interfaces or using a different mocking framework
