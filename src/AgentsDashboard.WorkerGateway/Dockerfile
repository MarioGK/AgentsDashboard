FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AgentsDashboard.slnx ./src/
COPY src/AgentsDashboard.WorkerGateway ./src/AgentsDashboard.WorkerGateway
COPY src/AgentsDashboard.Contracts ./src/AgentsDashboard.Contracts
COPY src/AgentsDashboard.ServiceDefaults ./src/AgentsDashboard.ServiceDefaults

RUN dotnet restore ./src/AgentsDashboard.WorkerGateway/AgentsDashboard.WorkerGateway.csproj
RUN dotnet publish ./src/AgentsDashboard.WorkerGateway/AgentsDashboard.WorkerGateway.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

RUN apt-get update && apt-get install -y --no-install-recommends docker.io gh wget ca-certificates && rm -rf /var/lib/apt/lists/* \
    && wget -qO/bin/grpc_health_probe https://github.com/grpc-ecosystem/grpc-health-probe/releases/download/v0.4.24/grpc_health_probe-linux-amd64 \
    && chmod +x /bin/grpc_health_probe

EXPOSE 5201
ENTRYPOINT ["dotnet", "AgentsDashboard.WorkerGateway.dll"]
