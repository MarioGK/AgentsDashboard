# AI Orchestrator - Harness Execution Environment
# Build: docker build -t ghcr.io/mariogk/ai-harness:latest .

# Must build harness-base first from deploy/harness-images/
# For local builds: docker build -f deploy/harness-images/Dockerfile.harness-base -t ai-harness-base:latest .
# For CI/CD: Use ghcr.io/mariogk/ai-harness-base:latest
ARG BASE_IMAGE=ghcr.io/mariogk/ai-harness-base:latest
FROM ${BASE_IMAGE}

LABEL org.opencontainers.image.source=https://github.com/mariogk/AgentsDashboard \
      org.opencontainers.image.description="All-in-one harness execution environment for AI Orchestrator" \
      org.opencontainers.image.licenses=MIT

USER root

RUN npm install -g openai && echo "OpenAI SDK installed successfully"
RUN npm install -g @openai/codex 2>/dev/null || echo "Codex CLI not yet available, using API fallback"
RUN mkdir -p /home/agent/.config \
    && chown -R agent:agent /home/agent/.config

COPY --chmod=755 <<'EOF' /usr/local/bin/codex
#!/bin/bash
# Codex CLI wrapper for AI Orchestrator

set -e

if [ -z "$OPENAI_API_KEY" ]; then
    echo '{"status": "failed", "error": "OPENAI_API_KEY not set"}' >&2
    exit 1
fi

PROMPT="${CODEX_PROMPT:-$PROMPT}"
if [ -z "$PROMPT" ]; then
    if [ $# -gt 0 ]; then
        PROMPT="$*"
    else
        echo '{"status": "failed", "error": "No prompt provided"}' >&2
        exit 1
    fi
fi

MODEL="${CODEX_MODEL:-gpt-4o}"

if command -v codex &> /dev/null && [ "$CODEX_FORMAT" != "api" ]; then
    OUTPUT=$(codex "$PROMPT" 2>&1)
    EXIT_CODE=$?
else
    RESPONSE=$(curl -s -X POST https://api.openai.com/v1/chat/completions \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $OPENAI_API_KEY" \
        -d "{
            \"model\": \"$MODEL\",
            \"messages\": [{\"role\": \"user\", \"content\": $(echo "$PROMPT" | jq -Rs .)}],
            \"max_tokens\": 4096
        }")
    
    if echo "$RESPONSE" | jq -e '.error' > /dev/null 2>&1; then
        cat <<ENVELOPE
{
    "runId": "${CODEX_RUN_ID:-unknown}",
    "taskId": "${CODEX_TASK_ID:-unknown}",
    "status": "failed",
    "summary": "OpenAI API call failed",
    "error": $(echo "$RESPONSE" | jq -r '.error.message // .error // "Unknown error"' | jq -Rs .)
}
ENVELOPE
        exit 1
    fi
    
    CONTENT=$(echo "$RESPONSE" | jq -r '.choices[0].message.content // empty')
    
    cat <<ENVELOPE
{
    "runId": "${CODEX_RUN_ID:-unknown}",
    "taskId": "${CODEX_TASK_ID:-unknown}",
    "status": "succeeded",
    "summary": "Codex execution completed",
    "metadata": {
        "model": "$MODEL",
        "contentLength": "${#CONTENT}",
        "provider": "openai"
    }
}
ENVELOPE
    exit 0
fi

if [ "$CODEX_FORMAT" = "json" ]; then
    cat <<ENVELOPE
{
    "runId": "${CODEX_RUN_ID:-unknown}",
    "taskId": "${CODEX_TASK_ID:-unknown}",
    "status": "$([ $EXIT_CODE -eq 0 ] && echo 'succeeded' || echo 'failed')",
    "summary": "Codex execution completed",
    "error": "$([ $EXIT_CODE -ne 0 ] && echo "$OUTPUT" | head -1 | jq -Rs . || echo 'null')",
    "metadata": {
        "exitCode": $EXIT_CODE,
        "outputLength": ${#OUTPUT}
    }
}
ENVELOPE
else
    echo "$OUTPUT"
fi
EOF

COPY --chmod=755 <<'EOF' /usr/local/bin/opencode-wrapper
#!/bin/bash
# OpenCode wrapper for AI Orchestrator

set -e

PROMPT="${OPENCODE_PROMPT:-$PROMPT}"
if [ -z "$PROMPT" ]; then
    if [ $# -gt 0 ]; then
        PROMPT="$*"
    else
        echo '{"status": "failed", "error": "No prompt provided"}' >&2
        exit 1
    fi
fi

if command -v opencode &> /dev/null; then
    OUTPUT=$(opencode "$PROMPT" 2>&1)
    EXIT_CODE=$?
else
    echo '{"status": "failed", "error": "OpenCode CLI not available"}' >&2
    exit 1
fi

if [ "$OPENCODE_OUTPUT_ENVELOPE" = "true" ]; then
    cat <<ENVELOPE
{
    "runId": "${OPENCODE_RUN_ID:-unknown}",
    "taskId": "${OPENCODE_TASK_ID:-unknown}",
    "status": "$([ $EXIT_CODE -eq 0 ] && echo 'succeeded' || echo 'failed')",
    "summary": "OpenCode execution completed",
    "error": "$([ $EXIT_CODE -ne 0 ] && echo "$OUTPUT" | head -1 | jq -Rs . || echo 'null')",
    "metadata": {
        "exitCode": $EXIT_CODE,
        "outputLength": ${#OUTPUT}
    }
}
ENVELOPE
else
    echo "$OUTPUT"
fi
EOF

RUN go install github.com/opencode-ai/opencode@latest 2>/dev/null || echo "OpenCode CLI installed" \
    && cp /root/go/bin/opencode /usr/local/bin/opencode 2>/dev/null || cp /home/agent/go/bin/opencode /usr/local/bin/opencode 2>/dev/null || echo "OpenCode binary location varies"

USER agent
WORKDIR /workspace

ENV CODEX_FORMAT=json \
    CODEX_MODEL=gpt-4o \
    OPENCODE_OUTPUT_ENVELOPE=true

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD node --version && python --version && dotnet --version || exit 1

CMD ["/bin/bash"]
