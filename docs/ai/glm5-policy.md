# GLM-5 Policy

## Policy
- All dashboard AI feature calls must use `glm-5`.
- No fallback model is allowed.
- If `glm-5` is blocked by account/plan/region, the feature fails with an actionable message.
- Z.ai plan docs indicate model availability differs by plan defaults; this repo still hard-locks runtime model to `glm-5`.

## Enforcement Points
- Provider settings:
  - `zai` model options are locked to `glm-5`.
  - `claude-code` model options are locked to `glm-5`.
- Dispatch pipeline:
  - `zai` harness forces `HARNESS_MODEL=glm-5` and `ZAI_MODEL=glm-5`.
  - `claude-code` with Z.ai Anthropic-compatible base URL forces `glm-5`.
- Dashboard AI feature services:
  - `LlmTornadoGatewayService` always calls `ChatModel.Zai.Glm.Glm5`.

## Failure Handling
- Return clear remediation:
  - "The current account/plan cannot use glm-5. Update your Z.ai plan/access and retry."
- Do not silently fall back to `glm-4.x`.
