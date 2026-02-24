FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/AgentsDashboard.slnx ./src/
COPY src/AgentsDashboard.ControlPlane ./src/AgentsDashboard.ControlPlane
COPY src/AgentsDashboard.Contracts ./src/AgentsDashboard.Contracts

RUN dotnet restore ./src/AgentsDashboard.ControlPlane/AgentsDashboard.ControlPlane.csproj
RUN dotnet publish ./src/AgentsDashboard.ControlPlane/AgentsDashboard.ControlPlane.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data \
    /home/agent/.ssh \
    /home/agent/.config/gh \
    /home/agent/.config/git \
    /home/agent/.config/opencode \
    /home/agent/.codex \
    && touch /home/agent/.gitconfig /home/agent/.git-credentials /home/agent/.netrc

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AgentsDashboard.ControlPlane.dll"]
