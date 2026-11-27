using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WorkerCSharp;

// Domain telemetry for the C# worker. The "TaskQueue.*" wildcard in Extensions.cs exports it.
// The Python and Node.js workers publish the same taskqueue.tasks.* instruments.
public sealed class WorkerTelemetry
{
    public const string Name = "TaskQueue.Worker.CSharp";
    public static readonly ActivitySource ActivitySource = new(Name, "1.0.0");

    private readonly Counter<long> _processed;
    private readonly Histogram<double> _duration;
    private readonly UpDownCounter<long> _active;
    private readonly Counter<long> _failed;
    private readonly Histogram<int> _reportWords;

    public WorkerTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name, "1.0.0");

        _processed = meter.CreateCounter<long>(
            "taskqueue.tasks.processed", unit: "{task}",
            description: "Tasks this worker finished, by task type and outcome (completed or failed).");

        _duration = meter.CreateHistogram<double>(
            "taskqueue.task.duration", unit: "s",
            description: "Time this worker spent on a task, from pickup to result, by task type and outcome.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.75, 1, 2, 5] });

        _active = meter.CreateUpDownCounter<long>(
            "taskqueue.tasks.active", unit: "{task}",
            description: "Tasks this worker is processing right now.");

        _failed = meter.CreateCounter<long>(
            "taskqueue.tasks.failed", unit: "{task}",
            description: "Tasks this worker could not process, by error type.");

        _reportWords = meter.CreateHistogram<int>(
            "taskqueue.report.words", unit: "{word}",
            description: "Words in each set of notes turned into a report.",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [25, 50, 100, 150, 200, 300, 500, 1000] });
    }

    public void TaskStarted(string taskType) => _active.Add(1, TaskType(taskType));

    public void TaskFinished(string taskType, string outcome, TimeSpan elapsed, string? errorType)
    {
        _active.Add(-1, TaskType(taskType));

        var tags = new TagList { { "taskqueue.task.type", taskType }, { "taskqueue.task.outcome", outcome } };
        _processed.Add(1, tags);
        _duration.Record(elapsed.TotalSeconds, tags);

        if (errorType is not null)
        {
            _failed.Add(1, new TagList { { "taskqueue.task.type", taskType }, { "error.type", errorType } });
        }
    }

    public void ReportGenerated(int words) => _reportWords.Record(words);

    private static KeyValuePair<string, object?> TaskType(string taskType) => new("taskqueue.task.type", taskType);
}
