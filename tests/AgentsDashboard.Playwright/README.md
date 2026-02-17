# AgentsDashboard Playwright Critical Flows

This suite covers only high-value smoke paths:

- Home dashboard load (`/`)
- Global search page load (`/search`)
- Orchestrator settings load (`/settings/orchestrator`)
- Workspace task-list surface visibility (`/workspace` -> `Recent Tasks`)

## Prerequisites

- Node.js 20+
- Running AgentsDashboard ControlPlane app

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
