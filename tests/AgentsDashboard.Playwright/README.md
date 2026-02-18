# AgentsDashboard Playwright Flows

This suite covers:

- Smoke checks:
  - Home dashboard load (`/`)
  - Global search page load (`/search`)
  - Orchestrator settings load (`/settings/orchestrator`)
  - Workspace task-list surface visibility (`/workspace` -> `Recent Tasks`)
- Full workflow check (`workflow-zai-e2e.spec.ts`):
  - Create repository
  - Save repository Z.ai secret
  - Create workspace task (`zai`)
  - Submit run guidance
  - Assert terminal run state (`Succeeded` or `Obsolete`)

## Prerequisites

- Node.js 20+
- Running AgentsDashboard ControlPlane app
- Local git remote fixture with an initialized `main` branch for repository creation tests
- Z.ai API key for real harness workflow execution

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

The full workflow test fails hard when required variables are missing.

- `PLAYWRIGHT_E2E_ZAI_API_KEY`: required Z.ai key
- `PLAYWRIGHT_E2E_REPO_REMOTE_PATH`: required absolute path to seeded git remote repo
- `PLAYWRIGHT_E2E_REPO_CLONE_ROOT`: required absolute directory where repository clones are created

Example:

```bash
cd tests/AgentsDashboard.Playwright
BASE_URL=http://127.0.0.1:5266 \
PLAYWRIGHT_E2E_ZAI_API_KEY=*** \
PLAYWRIGHT_E2E_REPO_REMOTE_PATH=/tmp/agentsdashboard-e2e/remote.git \
PLAYWRIGHT_E2E_REPO_CLONE_ROOT=/tmp/agentsdashboard-e2e/clones \
npm test
```
