# Feature Ideas (Z.ai + LLMTornado)

## High Priority
1. Agent Blueprint Generator
- Input: repository context + objective.
- Output: ready-to-run agent profile (system prompt, tools, guardrails, acceptance checks).
- Model: `glm-5` via `LlmTornadoGatewayService`.

2. AI Image Spec Builder
- Extend current Image Builder to also generate a dependency matrix (language, package managers, harness CLIs, security tools).
- Add one-click "harden image" pass (non-root user, healthcheck, pinned versions).

3. Workflow Step Authoring Assistant
- Generate DAG node prompts and edge conditions from natural language requirements.
- Validate generated steps against existing workflow schema before save.

## Medium Priority
1. Run Failure Auto-Triage
- Summarize failing run logs.
- Propose likely root cause and patch strategy.
- Create a draft follow-up task linked to the run.

2. Findings Dedup + Merge Suggestions
- Group semantically similar findings.
- Suggest canonical title, severity normalization, and merged remediation text.

3. Provider Key Health Panel
- Scheduled key validation for `llmtornado`, `zai`, and `anthropic`.
- Surface expiry/invalid states in dashboard alerts.

## Low Priority
1. Prompt Library Optimizer
- Score stored prompts by pass/fail outcomes and suggest edits.

2. Test Generation Assist
- Generate focused unit/integration test templates from changed files and findings.

3. Artifact Post-Processor
- Convert raw run outputs into structured summaries and reusable snippets.
