using Xunit;

#pragma warning disable CA1711
namespace Tests;

[CollectionDefinition("Environment")]
public class WorkflowEnvironmentCollection : ICollectionFixture<WorkflowEnvironment>
{
}