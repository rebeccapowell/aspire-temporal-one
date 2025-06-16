# aspire-temporal-one

This repository showcases a minimal integration of [Temporal](https://temporal.io/) with [\.NET Aspire](https://learn.microsoft.com/dotnet/aspire/). The original application included encrypted payload storage and automatic key rotation via Key Vault and Redis. This trimmed version removes those pieces and focuses on the core workflow functionality.

A step-by-step walkthrough of building this demo is available in [this blog post](https://rebecca-powell.com/posts/2025-06-09-combining-dotnet-aspire-and-temporal-part-1/).

---

## Project Layout

- **AppHost** – boots the Temporal development server and coordinates the other projects.
- **Api** – exposes endpoints for starting, signalling and querying a workflow.
- **Worker** – executes the workflow logic.
- **Workflows** – shared workflow and activity implementations.

A simplified `AppHost/Program.cs` looks like this:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var temporal = await builder.AddTemporalServerContainer("temporal", b => b
    .WithPort(7233)
    .WithHttpPort(7234)
    .WithMetricsPort(7235)
    .WithUiPort(8233)
    .WithLogLevel(LogLevel.Info));

temporal.PublishAsConnectionString();

builder.AddProject<Api>("api")
    .WithReference(temporal);
builder.AddProject<Worker>("worker")
    .WithReference(temporal);

builder.Build().Run();
```

Running `dotnet run --project AppHost` starts Temporal along with the API and Worker services. The API exposes simple endpoints to start, signal and query the workflow.

### Api/Program.cs

```csharp
var conn = builder.Configuration.GetConnectionString("temporal");
builder.Services
    .AddTemporalClient(conn, Constants.Namespace)
    .Configure(options =>
    {
        options.Interceptors = new[] { new TracingInterceptor() };
        options.Runtime = runtime;
    });

app.MapWorkflowEndpoints();
```

The workflow endpoints are defined in `WorkflowEndpoints.cs`:

```csharp
app.MapPost("/start/{message}", async ([FromRoute] string message, ITemporalClient client, WorkflowMetrics metrics) =>
{
    metrics.StartedCount.Add(1);
    var workflowId = $"simple-workflow-{Guid.NewGuid()}";
    await client.StartWorkflowAsync(
        (SimpleWorkflow wf) => wf.RunAsync(message),
        new WorkflowOptions(workflowId, Constants.TaskQueue));
    return TypedResults.Ok(new WorkflowStartResponse(workflowId));
});

app.MapPost("/signal/{workflowId}", async ([FromRoute] string workflowId, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle(workflowId);
    await handle.SignalAsync<SimpleWorkflow>(wf => wf.Continue());
    return TypedResults.Ok();
});

app.MapGet("/result/{workflowId}", async ([FromRoute] string workflowId, ITemporalClient client) =>
{
    var handle = client.GetWorkflowHandle(workflowId);
    var result = await handle.GetResultAsync<string>();
    return TypedResults.Ok(new WorkflowResultResponse(result));
});
```

### Worker/Program.cs

```csharp
builder.Services
    .AddTemporalClient(builder.Configuration.GetConnectionString("temporal"), Constants.Namespace)
    .Configure(options =>
    {
        options.Interceptors = new[] { new TracingInterceptor() };
        options.Runtime = runtime;
    });

builder.Services
    .AddHostedTemporalWorker(Constants.TaskQueue)
    .AddWorkflow<SimpleWorkflow>()
    .AddScopedActivities<Activities>();
```

## Example Workflow

```csharp
[Workflow]
public class SimpleWorkflow
{
    private bool _continueWorkflow;

    [WorkflowSignal]
    public Task Continue()
    {
        _continueWorkflow = true;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<string> RunAsync(string input)
    {
        Workflow.Logger.LogInformation("Workflow started with input: {input}", input);

        var result = await Workflow.ExecuteActivityAsync<Activities, string>(
            a => a.SimulateWork(input),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(120) });

        Workflow.Logger.LogInformation("Waiting for continue signal...");
        await Workflow.WaitConditionAsync(() => _continueWorkflow);

        var final = await Workflow.ExecuteActivityAsync<Activities, string>(
            a => a.FinalizeWork(result),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(120) });

        Workflow.Logger.LogInformation("Workflow completed.");
        return final;
    }
}
```

## Observability and Testing

All services are instrumented with OpenTelemetry using the helpers in `ServiceDefaults`. The test project uses the Temporal test server to execute the workflow end to end.

## Running the Demo

1. Launch the Aspire app:
   ```bash
   dotnet run --project AppHost
   ```
   This boots the Temporal dev server along with the API and Worker projects.
2. Kick off a workflow:
   ```bash
   curl -X POST http://localhost:5110/start/hello
   ```
3. Continue it when you are ready:
   ```bash
   curl -X POST http://localhost:5110/signal/<workflowId>
   ```
4. Fetch the result:
   ```bash
   curl http://localhost:5110/result/<workflowId>
   ```

The Aspire dashboard is available at `http://localhost:18888` and the Temporal Web UI at `http://localhost:8233`.


## Continuous Integration

The `.github/workflows/ci.yml` file defines a small pipeline. Every pull request restores dependencies, builds the solution and runs the test suite while collecting coverage.

---

Enjoy experimenting with TemporalAspireDemo!
