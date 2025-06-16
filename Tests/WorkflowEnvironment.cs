using System;
using System.Threading.Tasks;
using Temporalio.Client;
using Temporalio.Testing;
using Xunit;

namespace Tests;

public class WorkflowEnvironment : IAsyncLifetime
{
	private Temporalio.Testing.WorkflowEnvironment? _env;

	public ITemporalClient Client => _env?.Client ?? throw new InvalidOperationException("Environment not created");

	public async Task InitializeAsync()
	{
		_env = await Temporalio.Testing.WorkflowEnvironment.StartLocalAsync(new WorkflowEnvironmentStartLocalOptions
		{
			DevServerOptions = new DevServerOptions
			{
				ExtraArgs =
				[
					"--dynamic-config-value",
					"frontend.enableUpdateWorkflowExecution=true",
					"--dynamic-config-value",
					"frontend.enableExecuteMultiOperation=true"
				]
			}
		});
	}

	public async Task DisposeAsync()
	{
		if (_env != null) await _env.ShutdownAsync();
	}
}