# AI Orchestrator

Self-hosted AI orchestration platform on .NET 10. Blazor Server is the control plane; execution runs through CLI harnesses (`codex`, `opencode`) and dashboard AI feature calls via `LlmTornado`.

## Documentation
- Check the docs folder for better documentation

## Architecture Rules

- All intra communication MUST use MagicOnion.
- Use command/query handlers + domain services/events for orchestration.
- Keep transport DTOs at integration boundaries only.
- Backward compatibility is not required; keep only intended current behavior.
- We dont have to care about database lost of data and etc.

## Project Structure (Vertical Slices)

The codebase follows a **vertical slice architecture**. Each feature is self-contained in its own folder with all related code (services, models, gateways, components) colocated.

### Placement Rules

1. **Feature-specific code** goes in `Features/{FeatureName}/`
2. **Shared infrastructure** (used by multiple features) goes in `Infrastructure/`
3. **Layout components** stay in `Components/Layout/`
4. **DI extensions** for the entire project stay in `Extensions/`
5. **Gateway interfaces** live with the feature that *uses* them, not the feature that implements them
6. **One primary type per file** - no multiple classes/records in a single file
7. **Models subfolder** for domain models, DTOs, and snapshots specific to the feature

## Engineering Conventions

### Code Style

- File-scoped namespaces.
- Primary constructors where appropriate.
- Required properties for DTOs.
- Async methods use `Async` suffix.
- Keep extension methods/helper types in separate files.
- One primary type per file.
- No comments unless explicitly requested.
- Warnings are errors (`TreatWarningsAsErrors=true`); do not disable with `NoWarn`.
- All extensions must be inside of an Extensions folder instead of the root of the project.
- There should not be single use interfaces. 
- Every record or class must be in a single file, dont create a single file with multiple records and clasess.
- Do not ever use ! for ignoring that the value is null or not, always do the best practice for handling nullable types.

### Testing Policy

- Use integration tests with real service workflows instead of mock-based tests.
- Never use Moq in test projects.
- Never use FluentAssertions in test projects.
- Use TUnit assertions only (`Assert` APIs).

### LiteDB (Mandatory)

- Persistence is LiteDB-only (`LiteDB` NuGet `6.0.0-prerelease.75`);
- All store/repository APIs are async-only and must flow cancellation tokens.
- Use `IRepository<>` for collection access; do not reintroduce EF-style `DbContext` or single file database class.
- Serialize database access through the LiteDB execution path (`LiteDbDatabase`/`LiteDbExecutor`) for safe shutdown/disposal.
- Store run artifacts, workspace uploads, and image/file payloads in LiteDB (file storage + metadata), not filesystem paths.
- Workspace image ingestion compresses to WebP lossless before DB persistence and multimodal dispatch.

## Agent Workflow Rules

- Always use `main`; do not create feature branches.
- Assume that we dont have to save anything, this is a greenfield project and is not deployed yet.
- Build with `dotnet build src/AgentsDashboard.slnx -m --tl`.

## Local Run
- LAN: `https://192.168.10.101:5266`
- Health: `/alive`, `/ready`, `/health`
- Services must bind all interfaces (`0.0.0.0`) for LAN accessibility.
- Runtime persistence/log/workspace paths resolve under repo-root `data/`.

Preferred watch command:

```bash
DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1 DOTNET_WATCH_SUPPRESS_EMOJIS=1 DOTNET_WATCH_NONINTERACTIVE=1 DOTNET_USE_POLLING_FILE_WATCHER=1 DOTNET_WATCH_AUTO_RELOAD_WS_HOSTNAME=192.168.10.101 ASPNETCORE_ENVIRONMENT=Development dotnet watch --non-interactive --project src/AgentsDashboard.ControlPlane
```

To force non-interactive, always-restart-on-rude-edit behavior in unattended runs, keep `DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1`, `DOTNET_WATCH_NONINTERACTIVE=1`, and `dotnet watch --non-interactive`.

## Logging
- Use `ZLogger` in both ControlPlane and TaskRuntimeGateway.
- Prefer fewer, richer context lines over repetitive logs.

## Build & Test Quick Commands

```bash
# Build
dotnet build src/AgentsDashboard.slnx -m --tl

# Tests
dotnet test --solution src/AgentsDashboard.slnx

# Format gate
dotnet format src/AgentsDashboard.slnx --verify-no-changes --severity error

# Playwright full workflow gate (real Codex/OpenCode harness flow)
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_REPO_REMOTE_PATH=/abs/path/to/seeded-remote.git \
PLAYWRIGHT_E2E_REPO_CLONE_ROOT=/abs/path/to/clones \
npm test
```

MTP note: always pass `--project`, `--solution`, or direct `.csproj/.slnx`.
