# TemporalAspireDemo

Welcome to a guided tour of the small sample application we built together. Over a few pull requests we transformed a minimal Aspire/Temporal setup into a secure demo complete with a Key Vault emulator and automated tests. This README recounts that journey in a blog style, preserving the original instructions while highlighting the steps we took along the way.

---

## How It Started

Our first PR focused on showing how [Temporal](https://temporal.io/) can run alongside [\.NET Aspire](https://learn.microsoft.com/dotnet/aspire/). We used the [`InfinityFlow.Aspire.Temporal`](https://www.nuget.org/packages/InfinityFlow.Aspire.Temporal) package to spin up a local dev server directly from the `AppHost` project. The application consisted of:

- **AppHost** – boots Temporal and coordinates the other projects.
- **Api** – exposes endpoints for starting, signalling and querying a workflow.
- **Worker** – executes the workflow logic.
- **Workflows** – shared workflow and activity implementations.

A trimmed version of `AppHost/Program.cs` illustrates the basics:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var temporal = await builder.AddTemporalServerContainer("temporal", b => b
    .WithPort(7233)
    .WithHttpPort(7234)
    .WithMetricsPort(7235)
    .WithUiPort(8233)
    .WithLogLevel(LogLevel.Info));

temporal.PublishAsConnectionString();

var cache = builder.AddRedis("cache").WithRedisCommander();
cache.PublishAsConnectionString();

builder.AddProject<Api>("api")
    .WithReference(temporal)
    .WithReference(cache);
builder.AddProject<Worker>("worker")
    .WithReference(temporal)
    .WithReference(cache);

var app = builder.Build();
await app.StartAsync();
await app.WaitForShutdownAsync();
```

At this point running `dotnet run --project AppHost` launched Temporal and we could POST to `/start/{message}` to kick off a workflow. The worker used `Temporalio.Extensions.Hosting` to register the `SimpleWorkflow` and its activities.

---

## Encrypted Payloads via Key Vault

Next we tackled secure payload storage. Instead of persisting plaintext workflow data, we opted to encrypt everything using keys stored in an [Azure Key Vault Emulator](https://github.com/Azure/azure-key-vault-emulator). Two NuGet packages proved invaluable here:

- [`AzureKeyVaultEmulator.Aspire.Hosting`](https://www.nuget.org/packages/AzureKeyVaultEmulator.Aspire.Hosting)
- [`AzureKeyVaultEmulator.Client`](https://www.nuget.org/packages/AzureKeyVaultEmulator.Client)

We added the emulator as a service in `AppHost` along with a small seeder project that provisions a key when the app starts locally. The seeder also records the available key IDs in Redis so the apps don't fetch everything from Key Vault each time. Redis itself is configured via the hosting API:

```csharp
IResourceBuilder<ProjectResource>? seeder = null;
if (!builder.ExecutionContext.IsPublishMode)
{
    seeder = builder.AddProject<Projects.KeyVaultSeeder>("keyvaultseeder")
        .WithReference(keyVault)
        .WithReference(cache)
        .WithArgs("seed");
}

var api = builder.AddProject<Api>("api")
    .WithReference(temporal)
    .WithReference(keyVault)
    .WithReference(cache);

if (seeder is not null)
    api.WaitForCompletion(seeder);
```

With a key available we implemented `KeyVaultKeyProvider` and `KeyVaultEncryptionCodec` under `Workflows/Encryption` to load keys and wrap Temporal payloads. Here is the heart of the codec:

```csharp
public async Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads)
{
    var keyId = _provider.ActiveKeyId;
    var key = await _provider.GetActiveKeyAsync();
    var keyIdUtf8 = ByteString.CopyFromUtf8(keyId);
    return payloads.Select(p => new Payload
    {
        Metadata =
        {
            ["encoding"] = ByteString.CopyFromUtf8("binary/encrypted"),
            ["encryption-key-id"] = keyIdUtf8
        },
        Data = ByteString.CopyFrom(Encrypt(p.ToByteArray(), key))
    }).ToList();
}
```

Both the API and Worker projects configure their `DataConverter` to use this codec so that workflow inputs, results and signals are automatically encrypted and decrypted. The `KeyVaultKeyProvider` reads the list of key identifiers from Redis and fetches secret contents from Key Vault on demand. It checks Redis for the newest timestamp on each access so that the active key automatically updates after a rotation.

To demonstrate key rotation while the application runs we added a simple `KeyRollWorkflow` and exposed a `/keys/roll` endpoint in the API. Hitting this endpoint launches the workflow which generates a fresh secret in the Key Vault emulator.

---

## Codec Utilities

To help test the new codec we introduced two small endpoints. They simply accept Temporal payloads in JSON form and run them through the codec:

```csharp
public static class CodecEndpoints
{
    public static void MapCodecEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/encode", EncodeAsync).WithName("EncodePayloads");
        app.MapPost("/decode", DecodeAsync).WithName("DecodePayloads");
    }

    private static Task<IResult> EncodeAsync(HttpContext ctx, KeyVaultEncryptionCodec codec)
        => ApplyAsync(ctx, codec.EncodeAsync);

    private static Task<IResult> DecodeAsync(HttpContext ctx, KeyVaultEncryptionCodec codec)
        => ApplyAsync(ctx, codec.DecodeAsync);
}
```

These endpoints proved handy during development to verify that our codec works independently of a running workflow.

## Worker – executing the encrypted workflow

```csharp
var builder = Host.CreateApplicationBuilder(args);

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

var vaultUri = builder.Configuration.GetConnectionString("keyvault") ?? string.Empty;
builder.Services.AddAzureKeyVaultEmulator(vaultUri, secrets: true, keys: true, certificates: false);
builder.AddRedisClient("cache");
builder.Services.AddSingleton<KeyVaultKeyProvider>();
builder.Services.AddSingleton<KeyVaultEncryptionCodec>();

builder.Services
    .AddTemporalClient(builder.Configuration.GetConnectionString("temporal"), Constants.Namespace)
    .Configure(options =>
    {
        options.Interceptors = new[] { new TracingInterceptor() };
        options.Runtime = runtime;
    })
    .Configure<IServiceProvider>((options, sp) =>
    {
        options.DataConverter = DataConverter.Default with
        {
            PayloadCodec = sp.GetRequiredService<KeyVaultEncryptionCodec>()
        };
    });

builder.Services
    .AddHostedTemporalWorker(Constants.TaskQueue)
    .AddWorkflow<SimpleWorkflow>()
    .AddScopedActivities<Activities>();

var workflowMeter = new Meter("WorkflowMetrics");
builder.Services.AddSingleton(workflowMeter);
builder.Services.AddSingleton<WorkflowMetrics>();

var host = builder.Build();
host.Run();
```

Services can depend on `IConnectionMultiplexer` which is automatically provided when `AddRedisClient` is used:

```csharp
public class ExampleService(IConnectionMultiplexer connectionMux)
{
    // Use connectionMux to access Redis...
}
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


---

## Observability and Testing

From day one we instrumented everything with OpenTelemetry. Counters and histograms defined in `WorkflowMetrics` are registered in both the API and Worker, and traces flow through the Temporal SDK via [`Temporalio.Extensions.DiagnosticSource`](https://www.nuget.org/packages/Temporalio.Extensions.DiagnosticSource).

Writing tests for the encryption layer was another milestone. The `Tests` project spins up the Key Vault emulator and validates round‑trip encryption with xUnit:

```csharp
[Fact]
public async Task EncodeDecode_RoundTripsPayload()
{
    var hostBuilder = new HostBuilder();
    hostBuilder.ConfigureServices(s =>
        s.AddAzureKeyVaultEmulator("https://localhost:4997", secrets: true, keys: true, certificates: false));
    using var host = hostBuilder.Build();
    var client = host.Services.GetRequiredService<SecretClient>();
    var id = $"ns-default-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
    await client.SetSecretAsync(new KeyVaultSecret($"temporal-{id}", Convert.ToBase64String(CreateKey()))
    { Properties = { Tags = { ["namespace"] = Constants.Namespace } } });
    var redis = ConnectionMultiplexer.Connect("localhost:6379");
    var provider = new KeyVaultKeyProvider(client, redis, new ConfigurationBuilder().Build());
    var codec = new KeyVaultEncryptionCodec(provider);

    var payload = new Payload { Data = ByteString.CopyFromUtf8("hello") };
    var encoded = await codec.EncodeAsync(new[] { payload });
    var decoded = await codec.DecodeAsync(encoded);

    decoded.Single().Data.ToStringUtf8().ShouldBe("hello");
}
```

---

## Running the Demo

1. *(Optional)* run `./dev-setup.ps1` if you prefer to start the Temporal CLI manually.
2. Launch the Aspire app:
   ```bash
   dotnet run --project AppHost
   ```
   This boots the Temporal dev server, the Key Vault emulator and both the API and Worker projects.
3. Kick off a workflow:
   ```bash
   curl -X POST http://localhost:5110/start/hello
   ```
4. Continue it when you are ready:
   ```bash
   curl -X POST http://localhost:5110/signal/<workflowId>
   ```
5. Fetch the result:
   ```bash
   curl http://localhost:5110/result/<workflowId>
   ```
6. Try the codec utilities:
   ```bash
   curl -X POST http://localhost:5110/encode -H "Content-Type: application/json" -d '{"payloads":[]}'
   ```

The Aspire dashboard is available at `http://localhost:18888` and the Temporal Web UI at `http://localhost:8233`.

## Helm Charts via Aspirate

To make deployment as painless as local development we turned to the `aspirate` tool. It reads the Aspire manifest and generates a set of Helm charts. Running

```bash
aspirate generate AppHost/aspirate.json
```

produces the templates found in `k8s/base`. They mirror the same services we run locally so you can `helm install` the repo and get a working environment in Kubernetes.

## Continuous Integration

The `.github/workflows/ci.yml` file defines a small pipeline. Every pull request restores dependencies, builds, and executes the test suite while collecting coverage. Artifacts like the test results and coverage reports are uploaded for review.

---

## Wrapping Up

Through a handful of PRs we evolved this repository from a barebones workflow demo into a miniature environment that mirrors production features: encrypted payloads, repeatable tests and full telemetry. Along the way we leaned on packages like `InfinityFlow.Aspire.Temporal` to manage the Temporal server and the Key Vault emulator to avoid external dependencies. Feel free to explore the `k8s` directory for deployment examples or the test suite for more advanced usage.

Enjoy experimenting with TemporalAspireDemo – we certainly enjoyed building it together!

