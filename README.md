# AgentsDashboard

Production-oriented AI orchestration control plane built with .NET 10.

## Stack
- Blazor Server control plane with MudBlazor UI and BlazorMonaco editors
- Worker gateway over gRPC with ControlPlane-managed elastic worker pool
- Harness-only execution (`codex`, `opencode`, `claude-code`)
- SQLite persistence via EF Core
- YARP embedded in control plane
- Encrypted provider secret vault (Data Protection + SQLite)

## Solution
- `src/AgentsDashboard.ControlPlane`: UI, scheduler, SignalR, YARP
- `src/AgentsDashboard.WorkerGateway`: gRPC worker, queue, harness execution
- `src/AgentsDashboard.Contracts`: shared domain + gRPC contracts

## Local run (recommended)
1. Start control plane container:
```bash
docker compose up -d
```
2. Run control plane directly (optional alternative to containerized control plane):
```bash
dotnet run --project src/AgentsDashboard.ControlPlane
```
3. Open:
- Dashboard: `http://localhost:5266` (or the printed control-plane URL)

ControlPlane will spawn worker containers on demand through Docker socket.

## Playwright MCP (Codex)

Playwright MCP is configured in Codex for this workspace via `~/.codex/config.toml`:

```toml
[mcp_servers.playwright]
command = "playwright-mcp"
args = ["--headless", "--isolated", "--browser", "chrome"]
```

Quick checks:

```bash
codex mcp list
playwright-mcp --version
```

If Codex was already open before config changes, restart Codex so it reloads MCP server configuration.

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
- Task runs execute in container-sandboxed persistent task checkouts; git worktrees and PR-branch env orchestration are not part of runtime flow.

## Current feature set
- Project -> Repository -> Task hierarchy
- Task kinds: one-shot, cron, event-driven
- Manual task run trigger
- Scheduler loop for due one-shot/cron tasks
- Per-repo runs and findings inbox
- Findings retry from UI/application services
- Event-driven task triggers through internal orchestration services
- Repository secret management through application services
- Real-time run status/log events via SignalR
- Multi-worker gRPC dispatch and completion stream

## Notes
- v1 security model assumes a trusted self-hosted single-operator environment.
- Docker socket is mounted for privileged container operations and worker lifecycle orchestration.
- Encrypted secret vault protects provider credentials.
