var builder = DistributedApplication.CreateBuilder(args);

var mongodb = builder.AddContainer("mongodb", "mongo:8.0")
    .WithVolume("agentsdashboard-mongo-data", "/data/db")
    .WithEndpoint(port: 27017, targetPort: 27017, name: "mongo");

var victoriaMetrics = builder.AddContainer("victoria-metrics", "victoriametrics/victoria-metrics:latest")
    .WithArgs("-storageDataPath=/victoria-metrics-data")
    .WithVolume("agentsdashboard-vm-data", "/victoria-metrics-data")
    .WithHttpEndpoint(port: 8428, targetPort: 8428, name: "http");

var vmui = builder.AddContainer("vmui", "victoriametrics/vmui:latest")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
    .WithEnvironment("VMUI_BACKEND_URL", "http://victoria-metrics:8428");

var workerGateway = builder.AddProject<Projects.AgentsDashboard_WorkerGateway>("worker-gateway")
    .WithEnvironment("ConnectionStrings__mongodb", "mongodb://mongodb:27017")
    .WaitFor(mongodb);

builder.AddProject<Projects.AgentsDashboard_ControlPlane>("control-plane")
    .WithReference(workerGateway)
    .WithEnvironment("Orchestrator__MongoConnectionString", "mongodb://mongodb:27017")
    .WithEnvironment("Orchestrator__WorkerGrpcAddress", "http://worker-gateway:5201")
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://victoria-metrics:8428")
    .WaitFor(workerGateway)
    .WaitFor(mongodb);

builder.Build().Run();
