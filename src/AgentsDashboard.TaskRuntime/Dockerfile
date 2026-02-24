FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/AgentsDashboard.slnx ./src/
COPY src/AgentsDashboard.TaskRuntime ./src/AgentsDashboard.TaskRuntime
COPY src/AgentsDashboard.Contracts ./src/AgentsDashboard.Contracts

RUN dotnet restore ./src/AgentsDashboard.TaskRuntime/AgentsDashboard.TaskRuntime.csproj
RUN dotnet publish ./src/AgentsDashboard.TaskRuntime/AgentsDashboard.TaskRuntime.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ARG TARGETARCH

RUN apt-get update \
    && apt-get install -y --no-install-recommends git gh openssh-client ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    /home/agent/.ssh \
    /home/agent/.config/gh \
    /home/agent/.config/git \
    /home/agent/.config/opencode \
    /home/agent/.codex \
    && touch /home/agent/.gitconfig /home/agent/.git-credentials /home/agent/.netrc

EXPOSE 5201
ENTRYPOINT ["dotnet", "AgentsDashboard.TaskRuntime.dll"]
