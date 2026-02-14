var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.AgentsDashboard_ControlPlane>("control-plane")
    .WithEnvironment("Orchestrator__SqliteConnectionString", "Data Source=/data/orchestrator.db")
    .WithEnvironment("Orchestrator__WorkerGrpcAddress", "http://worker-gateway:5201")
    .WithEnvironment("Orchestrator__WorkerContainerName", "worker-gateway")
    .WithEnvironment("Orchestrator__WorkerDockerNetwork", "agentsdashboard")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:8428");

builder.Build().Run();
