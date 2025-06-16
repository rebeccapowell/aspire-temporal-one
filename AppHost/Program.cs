using InfinityFlow.Aspire.Temporal;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

var temporal = await builder.AddTemporalServerContainer("temporal", b => b
	.WithPort(7233)
	.WithHttpPort(7234)
	.WithMetricsPort(7235)
	.WithUiPort(8233)
	.WithLogLevel(LogLevel.Info)
);
temporal.PublishAsConnectionString();

var api = builder.AddProject<Api>("api")
	.WithReference(temporal);
var worker = builder.AddProject<Worker>("worker")
	.WithReference(temporal);

builder.Build().Run();