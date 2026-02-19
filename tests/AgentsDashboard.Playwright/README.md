# AgentsDashboard Playwright Flows

This suite covers:

- Smoke checks:
  - Home dashboard load (`/`)
  - Global search page load (`/search`)
- Orchestrator settings load (`/settings/task-runtimes`)
- Workspace task-list surface visibility (`/workspace` -> `Recent Tasks`)

## Prerequisites

- Node.js 20+
- Running AgentsDashboard ControlPlane app
- Local git remote fixture with an initialized `main` branch for repository creation tests
- Codex/OpenCode harness provider credentials for real harness workflow execution

## Install

```bash
cd tests/AgentsDashboard.Playwright
npm install
npm run install:browsers
```

## Run

Use `BASE_URL` to target the running app.

```bash
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 npm test
```

If `BASE_URL` is not provided, the suite defaults to `http://127.0.0.1:5266`.

## Full Workflow Environment Contract

- `PLAYWRIGHT_E2E_REPO_REMOTE_PATH`: required absolute path to seeded git remote repo
- `PLAYWRIGHT_E2E_REPO_CLONE_ROOT`: required absolute directory where repository clones are created

Example:

```bash
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_REPO_REMOTE_PATH=/tmp/agentsdashboard-e2e/remote.git \
PLAYWRIGHT_E2E_REPO_CLONE_ROOT=/tmp/agentsdashboard-e2e/clones \
npm test
```
