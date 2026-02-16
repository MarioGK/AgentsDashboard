# AgentsDashboard

Production-oriented AI orchestration control plane built with .NET 10.

## Stack
- Blazor Server control plane with MudBlazor UI and BlazorMonaco editors
- Worker gateway over gRPC with channel-based dispatch
- Harness-only execution (`codex`, `opencode`, `claude-code`)
- SQLite persistence via EF Core
- YARP embedded in control plane
- Aspire app host + OpenTelemetry-ready services
- Encrypted provider secret vault (Data Protection + SQLite)

## Solution
- `src/AgentsDashboard.ControlPlane`: UI, API, scheduler, SignalR, YARP
- `src/AgentsDashboard.WorkerGateway`: gRPC worker, queue, harness execution
- `src/AgentsDashboard.Contracts`: shared domain + gRPC contracts
- `src/AgentsDashboard.AppHost`: Aspire composition for local orchestration

## Local run (recommended)
1. Start control plane container:
```bash
docker compose up -d
```
2. (Optional) Run worker gateway directly for local dev:
```bash
dotnet run --project src/AgentsDashboard.WorkerGateway
```
3. Run control plane:
```bash
dotnet run --project src/AgentsDashboard.ControlPlane
```
4. Open:
- Dashboard: `http://localhost:5266` (or the printed control-plane URL)

## Full container stack
```bash
docker compose up --build
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
- Event-driven webhook trigger API (`POST /api/webhooks/{repositoryId}/{eventType?}`)
- Repository secret management API (`/api/repositories/{repositoryId}/secrets/*`)
- Real-time run status/log events via SignalR
- gRPC worker dispatch and completion stream

## Notes
- v1 security model assumes a trusted self-hosted single-operator environment.
- Docker socket is mounted for privileged container operations.
- Encrypted secret vault protects provider credentials.
