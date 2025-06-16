using Microsoft.Extensions.Logging;
using Temporalio.Workflows;

namespace Workflows;

[Workflow] // ← stays on the class
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

		// ▶️ Invoke your activity through the Temporal runtime
		var result = await Workflow.ExecuteActivityAsync<Activities, string>(
			a => a.SimulateWork(input),
			new ActivityOptions
			{
				StartToCloseTimeout = TimeSpan.FromSeconds(120)
			});

		Workflow.Logger.LogInformation("Waiting for continue signal...");
		await Workflow.WaitConditionAsync(() => _continueWorkflow);

		var final = await Workflow.ExecuteActivityAsync<Activities, string>(
			a => a.FinalizeWork(result),
			new ActivityOptions
			{
				StartToCloseTimeout = TimeSpan.FromSeconds(120)
			});

		Workflow.Logger.LogInformation("Workflow completed.");

		return final;
	}
}