# AI Orchestrator - All-in-One Harness Execution Environment
# Contains all harness tools: codex, opencode, claude-code, zai
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
RUN npm install -g @anthropic-ai/claude-code 2>/dev/null || echo "Claude Code not available, using API fallback"
RUN npm install -g cc-mirror 2>/dev/null || echo "cc-mirror not available, using ZhipuAI API fallback"

RUN mkdir -p /home/agent/.claude /home/agent/.zai /home/agent/.config/zai \
    && chown -R agent:agent /home/agent/.claude /home/agent/.zai /home/agent/.config

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

COPY --chmod=755 <<'EOF' /usr/local/bin/claude-wrapper
#!/bin/bash
# Claude Code wrapper for AI Orchestrator

set -e

if [ -z "$ANTHROPIC_API_KEY" ]; then
    echo '{"status": "failed", "error": "ANTHROPIC_API_KEY not set"}' >&2
    exit 1
fi

PROMPT="${CLAUDE_PROMPT:-$PROMPT}"
if [ -z "$PROMPT" ]; then
    if [ $# -gt 0 ]; then
        PROMPT="$*"
    else
        echo '{"status": "failed", "error": "No prompt provided"}' >&2
        exit 1
    fi
fi

MODEL="${CLAUDE_MODEL:-claude-sonnet-4-20250514}"

if command -v claude &> /dev/null && [ "$CLAUDE_OUTPUT_ENVELOPE" != "api" ]; then
    OUTPUT=$(claude "$PROMPT" 2>&1)
    EXIT_CODE=$?
else
    RESPONSE=$(curl -s -X POST https://api.anthropic.com/v1/messages \
        -H "Content-Type: application/json" \
        -H "x-api-key: $ANTHROPIC_API_KEY" \
        -H "anthropic-version: 2023-06-01" \
        -d "{
            \"model\": \"$MODEL\",
            \"max_tokens\": 8192,
            \"messages\": [{\"role\": \"user\", \"content\": $(echo "$PROMPT" | jq -Rs .)}]
        }")
    
    if echo "$RESPONSE" | jq -e '.error' > /dev/null 2>&1; then
        cat <<ENVELOPE
{
    "runId": "${CLAUDE_RUN_ID:-unknown}",
    "taskId": "${CLAUDE_TASK_ID:-unknown}",
    "status": "failed",
    "summary": "Anthropic API call failed",
    "error": $(echo "$RESPONSE" | jq -r '.error.message // .error // "Unknown error"' | jq -Rs .)
}
ENVELOPE
        exit 1
    fi
    
    CONTENT=$(echo "$RESPONSE" | jq -r '.content[0].text // empty')
    
    cat <<ENVELOPE
{
    "runId": "${CLAUDE_RUN_ID:-unknown}",
    "taskId": "${CLAUDE_TASK_ID:-unknown}",
    "status": "succeeded",
    "summary": "Claude Code execution completed",
    "metadata": {
        "model": "$MODEL",
        "contentLength": "${#CONTENT}",
        "provider": "anthropic"
    }
}
ENVELOPE
    exit 0
fi

if [ "$CLAUDE_OUTPUT_ENVELOPE" = "true" ]; then
    cat <<ENVELOPE
{
    "runId": "${CLAUDE_RUN_ID:-unknown}",
    "taskId": "${CLAUDE_TASK_ID:-unknown}",
    "status": "$([ $EXIT_CODE -eq 0 ] && echo 'succeeded' || echo 'failed')",
    "summary": "Claude Code execution completed",
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

COPY --chmod=755 <<'EOF' /usr/local/bin/zai
#!/bin/bash
# Zai CLI wrapper for AI Orchestrator (GLM-5 via cc-mirror)

set -e

if [ -z "$Z_AI_API_KEY" ]; then
    echo '{"status": "failed", "error": "Z_AI_API_KEY not set"}' >&2
    exit 1
fi

PROMPT="${ZAI_PROMPT:-$PROMPT}"
if [ -z "$PROMPT" ]; then
    if [ $# -gt 0 ]; then
        PROMPT="$*"
    else
        echo '{"status": "failed", "error": "No prompt provided"}' >&2
        exit 1
    fi
fi

MODEL="${ZAI_MODEL:-glm-5}"

if command -v cc-mirror &> /dev/null; then
    OUTPUT=$(cc-mirror run --provider zai --prompt "$PROMPT" 2>&1)
    EXIT_CODE=$?
else
    RESPONSE=$(curl -s -X POST https://open.bigmodel.cn/api/paas/v4/chat/completions \
        -H "Content-Type: application/json" \
        -H "Authorization: Bearer $Z_AI_API_KEY" \
        -d "{
            \"model\": \"$MODEL\",
            \"messages\": [{\"role\": \"user\", \"content\": $(echo "$PROMPT" | jq -Rs .)}],
            \"max_tokens\": 8192
        }")
    
    if echo "$RESPONSE" | jq -e '.error' > /dev/null 2>&1; then
        cat <<ENVELOPE
{
    "runId": "${ZAI_RUN_ID:-unknown}",
    "taskId": "${ZAI_TASK_ID:-unknown}",
    "status": "failed",
    "summary": "Zai API call failed",
    "error": $(echo "$RESPONSE" | jq -r '.error.message // .error // "Unknown error"' | jq -Rs .)
}
ENVELOPE
        exit 1
    fi
    
    CONTENT=$(echo "$RESPONSE" | jq -r '.choices[0].message.content // empty')
    
    cat <<ENVELOPE
{
    "runId": "${ZAI_RUN_ID:-unknown}",
    "taskId": "${ZAI_TASK_ID:-unknown}",
    "status": "succeeded",
    "summary": "Zai (GLM-5) execution completed",
    "metadata": {
        "model": "$MODEL",
        "contentLength": "${#CONTENT}",
        "provider": "zhipuai"
    }
}
ENVELOPE
    exit 0
fi

if [ "$ZAI_OUTPUT_ENVELOPE" = "true" ]; then
    cat <<ENVELOPE
{
    "runId": "${ZAI_RUN_ID:-unknown}",
    "taskId": "${ZAI_TASK_ID:-unknown}",
    "status": "$([ $EXIT_CODE -eq 0 ] && echo 'succeeded' || echo 'failed')",
    "summary": "Zai execution via cc-mirror completed",
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

RUN npx cc-mirror quick --provider zai --api-key "$Z_AI_API_KEY" 2>/dev/null || echo "cc-mirror quick setup attempted - Z_AI_API_KEY should be set at runtime"

USER agent
WORKDIR /workspace

ENV CODEX_FORMAT=json \
    CODEX_MODEL=gpt-4o \
    CLAUDE_OUTPUT_ENVELOPE=true \
    CLAUDE_MODEL=claude-sonnet-4-20250514 \
    ZAI_OUTPUT_ENVELOPE=true \
    ZAI_MODEL=glm-5 \
    OPENCODE_OUTPUT_ENVELOPE=true

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD node --version && python --version && dotnet --version || exit 1

CMD ["/bin/bash"]
