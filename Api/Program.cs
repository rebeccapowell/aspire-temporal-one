using System.Diagnostics.Metrics;
using Api.Endpoints;
using Api.Instrumentation;
using Temporalio.Extensions.DiagnosticSource;
using Temporalio.Extensions.OpenTelemetry;
using Temporalio.Runtime;
using Workflows;

var builder = WebApplication.CreateBuilder(args);

// Register OTEL early to wire everything up
builder.AddServiceDefaults(
	metrics =>
	{
		metrics.AddMeter("WorkflowMetrics");
		metrics.AddMeter("Temporal.Client");
	},
	tracing =>
	{
		tracing
			.AddSource(TracingInterceptor.ClientSource.Name)
			.AddSource(TracingInterceptor.WorkflowsSource.Name)
			.AddSource(TracingInterceptor.ActivitiesSource.Name);
	});

// API-specific services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();

// Register WorkflowMetrics once
var workflowMeter = new Meter("WorkflowMetrics");
builder.Services.AddSingleton(workflowMeter);
builder.Services.AddSingleton<WorkflowMetrics>();

// Create Temporal runtime with metrics support
var temporalMeter = new Meter("Temporal.Client");
var runtime = new TemporalRuntime(new TemporalRuntimeOptions
{
	Telemetry = new TelemetryOptions
	{
		Metrics = new MetricsOptions { CustomMetricMeter = new CustomMetricMeter(temporalMeter) }
	}
});

// Temporal client setup
var conn = builder.Configuration.GetConnectionString("temporal");
builder.Services
	.AddTemporalClient(conn, Constants.Namespace)
	.Configure(options =>
	{
		options.Interceptors = new[] { new TracingInterceptor() };
		options.Runtime = runtime;
	});

// Build the app
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

// We need CORS so that the browser can access this endpoint from a
// different origin
app.UseCors(
	builder => builder
		.WithHeaders("content-type", "x-namespace")
		.WithMethods("POST")
		.WithOrigins("http://localhost:8233", "https://cloud.temporal.io"));

//if (!app.Environment.IsDevelopment())
//{
//    app.UseHttpsRedirection();
//}
app.MapWorkflowEndpoints(); // Workflow triggering endpoint
app.MapHealthChecks("/health");

app.Run();