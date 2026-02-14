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

RUN apt-get update && apt-get install -y --no-install-recommends docker.io gh && rm -rf /var/lib/apt/lists/*

EXPOSE 5201
ENTRYPOINT ["dotnet", "AgentsDashboard.WorkerGateway.dll"]
