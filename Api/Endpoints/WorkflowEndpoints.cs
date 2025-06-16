using Api.Instrumentation;
using Microsoft.AspNetCore.Mvc;
using Temporalio.Client;
using Workflows;

namespace Api.Endpoints;

public static class WorkflowEndpoints
{
	public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
	{
		app.MapPost("/start/{message}", async (
				[FromRoute] string message,
				ITemporalClient client,
				WorkflowMetrics metrics) =>
			{
				metrics.StartedCount.Add(1);

				var workflowId = $"simple-workflow-{Guid.NewGuid()}";
				await client.StartWorkflowAsync(
					(SimpleWorkflow wf) => wf.RunAsync(message),
					new WorkflowOptions(workflowId, Constants.TaskQueue));

				var response = new WorkflowStartResponse(workflowId);
				return TypedResults.Ok(response);
			})
			.WithName("StartWorkflow")
			.WithTags("Workflows")
			.WithOpenApi();

		app.MapPost("/signal/{workflowId}", async ([FromRoute] string workflowId, ITemporalClient client) =>
			{
				var handle = client.GetWorkflowHandle(workflowId);
				await handle.SignalAsync<SimpleWorkflow>(wf => wf.Continue());
				return TypedResults.Ok();
			})
			.WithName("SignalWorkflow")
			.WithTags("Workflows")
			.WithOpenApi();

		app.MapGet("/result/{workflowId}", async ([FromRoute] string workflowId, ITemporalClient client) =>
			{
				var handle = client.GetWorkflowHandle(workflowId);
				var result = await handle.GetResultAsync<string>();
				return TypedResults.Ok(new WorkflowResultResponse(result));
			})
			.WithName("WorkflowResult")
			.WithTags("Workflows")
			.WithOpenApi();
	}

	public record WorkflowStartResponse(string WorkflowId);

	public record WorkflowResultResponse(string Result);
}