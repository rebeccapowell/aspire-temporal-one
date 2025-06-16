using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Temporalio.Client;
using Temporalio.Worker;
using Workflows;
using Workflows.Instrumentation;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

public class SimpleWorkflowTests(ITestOutputHelper output, WorkflowEnvironment env)
	: WorkflowEnvironmentTestBase(output, env)
{
	[Fact]
	public async Task RunWorkflow_CompletesAfterSignal()
	{
		if (Environment.GetEnvironmentVariable("RUN_INTEGRATION_TESTS") is null)
			return;
		var metrics = new WorkflowMetrics(new Meter("Test"));
		var hostBuilder = new HostBuilder();
		using var host = hostBuilder.Build();
		var activities = new Activities(metrics);

		using var worker = new TemporalWorker(
			Env.Client,
			new TemporalWorkerOptions(Constants.TaskQueue)
				.AddActivity(activities.SimulateWork)
				.AddActivity(activities.FinalizeWork)
				.AddWorkflow<SimpleWorkflow>());

		await worker.ExecuteAsync(async () =>
		{
			var workflowId = $"workflow-{Guid.NewGuid()}";
			var handle = await Client.StartWorkflowAsync(
				(SimpleWorkflow wf) => wf.RunAsync("hello"),
				new WorkflowOptions(workflowId, Constants.TaskQueue));

			await Task.Delay(1000);
			await handle.SignalAsync(wf => wf.Continue());

			var result = await handle.GetResultAsync();
			result.ShouldBe("Finalized: Processed: hello");
		});
	}
}