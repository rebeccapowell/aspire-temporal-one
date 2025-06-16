using System.Diagnostics.Metrics;

namespace Workflows.Instrumentation;

public class WorkflowMetrics
{
	public WorkflowMetrics(Meter meter)
	{
		StartedCount = meter.CreateCounter<long>("workflow.started.count");
		ActivityDurationMs = meter.CreateHistogram<double>("workflow.activity.duration.ms");
	}

	public Counter<long> StartedCount { get; }
	public Histogram<double> ActivityDurationMs { get; }
}