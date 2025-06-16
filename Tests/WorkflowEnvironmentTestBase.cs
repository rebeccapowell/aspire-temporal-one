using Temporalio.Client;
using Xunit;
using Xunit.Abstractions;

namespace Tests;

[Collection("Environment")]
public abstract class WorkflowEnvironmentTestBase : TestBase
{
	protected WorkflowEnvironmentTestBase(ITestOutputHelper output, WorkflowEnvironment env)
		: base(output)
	{
		Env = env;
		var newOptions = (TemporalClientOptions)env.Client.Options.Clone();
		newOptions.LoggerFactory = LoggerFactory;
		Client = new TemporalClient(env.Client.Connection, newOptions);
	}

	protected WorkflowEnvironment Env { get; }

	protected ITemporalClient Client { get; }
}