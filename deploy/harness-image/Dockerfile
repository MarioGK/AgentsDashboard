# AI Orchestrator - All-in-One Harness Execution Environment
# Contains all harness tools: codex, opencode, claude-code, zai
# Build: docker build -t ghcr.io/mariogk/ai-harness:latest .

FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NODE_VERSION=20 \
    BUN_VERSION=1.2.1 \
    PYTHON_VERSION=3.12 \
    GO_VERSION=1.23.6 \
    HOME=/home/agent

LABEL org.opencontainers.image.source=https://github.com/mariogk/AgentsDashboard \
      org.opencontainers.image.description="All-in-one harness execution environment for AI Orchestrator" \
      org.opencontainers.image.licenses=MIT

RUN apt-get update && apt-get install -y \
    ca-certificates \
    curl \
    wget \
    git \
    jq \
    unzip \
    zip \
    gnupg \
    lsb-release \
    software-properties-common \
    apt-transport-https \
    build-essential \
    sudo \
    ripgrep \
    fd-find \
    && rm -rf /var/lib/apt/lists/*

RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && npm install -g npm@latest \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://bun.sh/install | bash -s "bun-v${BUN_VERSION}" \
    && mv /root/.bun/bin/bun /usr/local/bin/bun \
    && chmod +x /usr/local/bin/bun

RUN add-apt-repository ppa:deadsnakes/ppa -y \
    && apt-get update \
    && apt-get install -y \
    python${PYTHON_VERSION} \
    python${PYTHON_VERSION}-dev \
    python${PYTHON_VERSION}-venv \
    python3-pip \
    && update-alternatives --install /usr/bin/python python /usr/bin/python${PYTHON_VERSION} 1 \
    && update-alternatives --install /usr/bin/python3 python3 /usr/bin/python${PYTHON_VERSION} 1 \
    && rm -rf /var/lib/apt/lists/*

RUN wget https://go.dev/dl/go${GO_VERSION}.linux-amd64.tar.gz \
    && tar -C /usr/local -xzf go${GO_VERSION}.linux-amd64.tar.gz \
    && rm go${GO_VERSION}.linux-amd64.tar.gz
ENV PATH="/usr/local/go/bin:/root/go/bin:${PATH}"
ENV GOPATH="/root/go"

RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | tee /etc/apt/sources.list.d/github-cli.list > /dev/null \
    && apt-get update \
    && apt-get install -y gh \
    && rm -rf /var/lib/apt/lists/*

RUN install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null \
    && apt-get update \
    && apt-get install -y docker-ce-cli docker-compose-plugin \
    && rm -rf /var/lib/apt/lists/*

RUN pip3 install --no-cache-dir \
    requests \
    httpx \
    pytest \
    pyyaml \
    black \
    ruff

RUN npx playwright@latest install --with-deps chromium || echo "Playwright installation skipped"

RUN npm install -g openai 2>/dev/null || echo "OpenAI SDK installed"
RUN npm install -g @openai/codex 2>/dev/null || echo "Codex CLI installation attempted"
RUN npm install -g @anthropic-ai/claude-code 2>/dev/null || echo "Claude Code installation attempted"
RUN npm install -g cc-mirror 2>/dev/null || echo "cc-mirror installation attempted"
RUN npx cc-mirror quick --provider zai 2>/dev/null || echo "Zai (claude-code mirror) installation attempted"
RUN go install github.com/opencode-ai/opencode@latest 2>/dev/null || echo "OpenCode installation attempted"

RUN useradd -m -s /bin/bash -u 1000 agent \
    && echo "agent ALL=(ALL) NOPASSWD:ALL" >> /etc/sudoers \
    && usermod -aG docker agent || true

RUN mkdir -p /workspace /artifacts /workspace/.config /workspace/.cache \
    /home/agent/.claude /home/agent/.zai \
    && chown -R agent:agent /workspace /artifacts /home/agent

USER agent
WORKDIR /workspace

ENV PATH="/home/agent/.local/bin:/home/agent/go/bin:${PATH}" \
    GOPATH="/home/agent/go" \
    CODEX_FORMAT=json \
    CODEX_MODEL=gpt-4o \
    CLAUDE_OUTPUT_ENVELOPE=true \
    CLAUDE_MODEL=claude-sonnet-4-20250514 \
    ZAI_OUTPUT_ENVELOPE=true \
    ZAI_MODEL=glm-5

USER root
RUN su - agent -c "go install github.com/opencode-ai/opencode@latest" 2>/dev/null || echo "OpenCode CLI installed for agent"
USER agent

HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD node --version && python --version && dotnet --version || exit 1

CMD ["/bin/bash"]
