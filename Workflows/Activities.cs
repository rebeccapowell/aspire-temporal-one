using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Temporalio.Activities;
using Workflows.Instrumentation;

namespace Workflows;

public class Activities
{
	private const string SecretPrefix = "temporal-";
	private static readonly string CacheKey = $"temporal:{Constants.Namespace}:keys";

	private readonly WorkflowMetrics _metrics;

	// ✅ DI is fine here
	public Activities(WorkflowMetrics metrics)
	{
		_metrics = metrics;
	}

	[Activity]
	public async Task<string> SimulateWork(string input)
	{
		ActivityExecutionContext.Current.Logger.LogInformation("Activity running with input: {input}", input);

		var sw = Stopwatch.StartNew();
		await Task.Delay(1000, ActivityExecutionContext.Current.CancellationToken);
		sw.Stop();

		_metrics.ActivityDurationMs.Record(sw.Elapsed.TotalMilliseconds);

		ActivityExecutionContext.Current.Logger.LogInformation("Activity completed.");

		return $"Processed: {input}";
	}

	[Activity]
	public async Task<string> FinalizeWork(string input)
	{
		ActivityExecutionContext.Current.Logger.LogInformation("Final activity running with input: {input}", input);

		var sw = Stopwatch.StartNew();
		await Task.Delay(1000, ActivityExecutionContext.Current.CancellationToken);
		sw.Stop();

		_metrics.ActivityDurationMs.Record(sw.Elapsed.TotalMilliseconds);

		ActivityExecutionContext.Current.Logger.LogInformation("Final activity completed.");

		return $"Finalized: {input}";
	}
}