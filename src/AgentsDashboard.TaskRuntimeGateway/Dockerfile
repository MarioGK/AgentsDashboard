FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY src/AgentsDashboard.slnx ./src/
COPY src/AgentsDashboard.TaskRuntimeGateway ./src/AgentsDashboard.TaskRuntimeGateway
COPY src/AgentsDashboard.Contracts ./src/AgentsDashboard.Contracts

RUN dotnet restore ./src/AgentsDashboard.TaskRuntimeGateway/AgentsDashboard.TaskRuntimeGateway.csproj
RUN dotnet publish ./src/AgentsDashboard.TaskRuntimeGateway/AgentsDashboard.TaskRuntimeGateway.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ARG TARGETARCH

RUN apt-get update && apt-get install -y --no-install-recommends docker.io gh ca-certificates && rm -rf /var/lib/apt/lists/*

EXPOSE 5201
ENTRYPOINT ["dotnet", "AgentsDashboard.TaskRuntimeGateway.dll"]
