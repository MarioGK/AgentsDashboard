# Z.ai + Claude Code Setup

## Goal
Route Claude Code through the Z.ai Anthropic-compatible endpoint and run with `glm-5`.

## Prerequisites
- Z.ai API key.
- Claude Code installed (`npm install -g @anthropic-ai/claude-code`).
- Node.js 18+.

## Recommended Setup (Official Coding Helper)
1. Run:
   - `npx @z_ai/coding-helper`
2. Follow prompts to configure Claude Code with your Z.ai key.
3. Open a new terminal and run `claude`.
4. Validate with `/status` and a small coding task.

## Manual Setup
1. Set env vars:
   - `ANTHROPIC_AUTH_TOKEN=<YOUR_Z_AI_KEY>`
   - `ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic`
2. Optional: place the same values in `~/.claude/settings.json` under `"env"`.
3. Restart terminal session and run `claude`.

## Model Mapping Note
- Z.ai Coding Plan defaults may map Claude model slots to GLM-4.7/GLM-4.5-Air depending on plan defaults.
- AgentsDashboard intentionally overrides this and enforces `glm-5` for Z.ai paths.

## Dashboard Mapping
- Provider secret `llmtornado` maps to:
  - `ANTHROPIC_AUTH_TOKEN`
  - `ANTHROPIC_API_KEY`
  - `ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic`
  - `Z_AI_API_KEY`
- Global key scope: `repositoryId=global`.
