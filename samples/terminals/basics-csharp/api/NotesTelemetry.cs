using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Notes.Api;

/// <summary>
/// Domain traces and metrics for the notes API. ServiceDefaults exports the "Contoso.*" sources and meters.
/// Metric tags are limited to the operation and its outcome; note IDs only appear on spans and logs.
/// </summary>
public sealed class NotesTelemetry
{
    public const string Name = "Contoso.Notes";
    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _operations;
    private readonly Histogram<int> _textLength;
    private long _storedNotes = -1;

    public NotesTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _operations = meter.CreateCounter<long>(
            "notes.operations", unit: "{operation}",
            description: "Note operations handled by the API, by operation (list, read, write, delete, seed) and outcome.");

        _textLength = meter.CreateHistogram(
            "notes.text.length", unit: "{character}",
            description: "Length of the note text written through the API, by whether the note was created or replaced.",
            advice: new InstrumentAdvice<int> { HistogramBucketBoundaries = [10, 20, 40, 60, 80, 100, 250, 500, 1000] });

        // Redis REPL edits bypass the API, so this reports the count the API last saw rather than polling Redis.
        meter.CreateObservableGauge(
            "notes.stored", ObserveStoredNotes, unit: "{note}",
            description: "Notes in the Redis hash as last seen by the API: set by each list, then adjusted by creates and deletes.");
    }

    public NoteOperation StartOperation(string operation, string? noteId = null)
    {
        var activity = ActivitySource.StartActivity($"notes.{operation}");
        activity?.SetTag("notes.operation", operation);
        if (noteId is not null)
        {
            activity?.SetTag("note.id", noteId);
        }
        return new NoteOperation(this, operation, activity);
    }

    public void OperationCompleted(string operation, string outcome) =>
        _operations.Add(1, new TagList { { "notes.operation", operation }, { "notes.outcome", outcome } });

    public void TextWritten(int length, bool created) =>
        _textLength.Record(length, new KeyValuePair<string, object?>("notes.outcome", created ? "created" : "replaced"));

    public void NotesListed(int count) => Interlocked.Exchange(ref _storedNotes, count);

    public void StoredNotesChanged(int delta)
    {
        long current;
        do
        {
            current = Volatile.Read(ref _storedNotes);
            if (current < 0)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _storedNotes, Math.Max(0, current + delta), current) != current);
    }

    private IEnumerable<Measurement<long>> ObserveStoredNotes() =>
        Volatile.Read(ref _storedNotes) is >= 0 and var count ? [new Measurement<long>(count)] : [];
}

/// <summary>A domain span plus its operation count. Disposing without an outcome marks the operation as failed.</summary>
public sealed class NoteOperation(NotesTelemetry telemetry, string operation, Activity? activity) : IDisposable
{
    private string? _outcome;

    public Activity? Activity => activity;

    public void Complete(string outcome, string? eventName = null)
    {
        _outcome = outcome;
        activity?.SetTag("notes.outcome", outcome);
        if (eventName is not null)
        {
            activity?.AddEvent(new ActivityEvent(eventName));
        }
    }

    public void Dispose()
    {
        if (_outcome is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "The notes store request failed.");
        }
        telemetry.OperationCompleted(operation, _outcome ?? "failed");
        activity?.Dispose();
    }
}
