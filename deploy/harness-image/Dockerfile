# AI Orchestrator - All-in-One Harness Execution Environment
# Contains all harness tools: codex, opencode, claude-code, zai
# Build: docker build -t ghcr.io/mariogk/ai-harness:latest .

# Must build harness-base first from deploy/harness-images/
FROM ai-harness-base:latest

LABEL org.opencontainers.image.source=https://github.com/mariogk/AgentsDashboard \
      org.opencontainers.image.description="All-in-one harness execution environment for AI Orchestrator" \
      org.opencontainers.image.licenses=MIT

USER root

RUN apt-get update && apt-get install -y \
    ripgrep \
    fd-find \
    && rm -rf /var/lib/apt/lists/*

RUN npm install -g openai && echo "OpenAI SDK installed successfully"
RUN npm install -g @openai/codex 2>/dev/null || echo "Codex CLI not yet available, using API fallback"
RUN npm install -g @anthropic-ai/claude-code 2>/dev/null || echo "Claude Code not available, using API fallback"
RUN npm install -g cc-mirror 2>/dev/null || echo "cc-mirror not available, using ZhipuAI API fallback"

RUN mkdir -p /home/agent/.claude /home/agent/.zai /home/agent/.config/zai \
    && chown -R agent:agent /home/agent/.claude /home/agent/.zai /home/agent/.config

USER agent
WORKDIR /workspace

ENV CODEX_FORMAT=json \
    CODEX_MODEL=gpt-4o \
    CLAUDE_OUTPUT_ENVELOPE=true \
    CLAUDE_MODEL=claude-sonnet-4-20250514 \
    ZAI_OUTPUT_ENVELOPE=true \
    ZAI_MODEL=glm-5

RUN go install github.com/opencode-ai/opencode@latest 2>/dev/null || echo "OpenCode CLI installed"

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD node --version && python --version && dotnet --version || exit 1

CMD ["/bin/bash"]
