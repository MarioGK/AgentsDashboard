# AgentsDashboard

Production-oriented AI orchestration control plane built with .NET 10.

## Stack
- Blazor Server control plane with MudBlazor UI and BlazorMonaco editors
- Worker gateway over gRPC with channel-based dispatch
- Harness-only execution (`codex`, `opencode`, `claude-code`)
- MongoDB persistence
- YARP embedded in control plane
- Aspire app host + OpenTelemetry-ready services
- VictoriaMetrics + VMUI for metrics
- Cookie auth + RBAC (`viewer`, `operator`, `admin`)
- Encrypted provider secret vault (Data Protection + MongoDB)

## Solution
- `src/AgentsDashboard.ControlPlane`: UI, API, scheduler, SignalR, YARP
- `src/AgentsDashboard.WorkerGateway`: gRPC worker, queue, harness execution
- `src/AgentsDashboard.Contracts`: shared domain + gRPC contracts
- `src/AgentsDashboard.AppHost`: Aspire composition for local orchestration

## Local run (recommended)
1. Start infrastructure:
```bash
docker compose -f deploy/docker-compose.yml up -d mongodb victoria-metrics vmui
```
2. Run worker:
```bash
dotnet run --project src/AgentsDashboard.WorkerGateway
```
3. Run control plane:
```bash
dotnet run --project src/AgentsDashboard.ControlPlane
```
4. Open:
- Dashboard: `http://localhost:5266` (or the printed control-plane URL)
- VMUI: `http://localhost:8081`

## Full container stack
```bash
docker compose -f deploy/docker-compose.yml up --build
```
Control plane: `http://localhost:8080`

## Harness behavior
- Task definitions require a shell command to execute.
- Commands should emit a JSON envelope when possible:
```json
{
  "status": "succeeded",
  "summary": "All tests passed",
  "error": "",
  "artifacts": []
}
```
- If output is not JSON, worker wraps stdout/stderr into the normalized envelope.
- If `AUTO_CREATE_PR=true`, worker can call `gh pr create` with run-provided env (`GH_REPO`, `PR_BRANCH`, `PR_TITLE`, `PR_BODY`).

## Current feature set
- Project -> Repository -> Task hierarchy
- Task kinds: one-shot, cron, event-driven (event ingestion endpoint is scaffold-ready)
- Manual task run trigger
- Scheduler loop for due one-shot/cron tasks
- Per-repo runs and findings inbox
- Findings retry API (`POST /api/findings/{findingId}/retry`)
- Event-driven webhook trigger API (`POST /api/webhooks/{repositoryId}/{token}`)
- Repository secret management API (`/api/repositories/{repositoryId}/secrets/*`)
- Real-time run status/log events via SignalR
- gRPC worker dispatch and completion stream

## Authentication
- Default users are configured in `src/AgentsDashboard.ControlPlane/appsettings.json`.
- Change the default passwords before production use.
- Roles:
  - `viewer`: read-only APIs and dashboard
  - `operator`: can create/update/run/retry tasks
  - `admin`: includes operator privileges

## Notes
- v1 security model assumes a trusted self-hosted single-operator environment.
- Docker socket is mounted for privileged container operations.
- API auth, RBAC, and encrypted secret vault are planned hardening steps.
