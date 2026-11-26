using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace VolumeMount.BlazorWeb.Inspections;

/// <summary>
/// Domain traces and metrics for equipment inspections. The names follow the "VolumeMount.*"
/// convention that ServiceDefaults registers with OpenTelemetry.
/// </summary>
public sealed class InspectionTelemetry
{
    public const string Name = "VolumeMount.Inspections";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _recordsCreated;
    private readonly Histogram<long> _attachmentSize;
    private readonly Counter<long> _uploadsFailed;
    private readonly UpDownCounter<long> _uploadsInProgress;

    public InspectionTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _recordsCreated = meter.CreateCounter<long>(
            "inspections.records.created",
            unit: "{inspection}",
            description: "Inspection records written to SQL Server.");

        _attachmentSize = meter.CreateHistogram(
            "inspections.attachment.size",
            unit: "By",
            description: "Size of photos and documents uploaded to Blob Storage.",
            advice: new InstrumentAdvice<long> { HistogramBucketBoundaries = [1_024, 4_096, 16_384, 65_536, 262_144, 1_048_576, 4_194_304, 10_485_760] });

        _uploadsFailed = meter.CreateCounter<long>(
            "inspections.attachment.uploads.failed",
            unit: "{upload}",
            description: "Attachment uploads to Blob Storage that failed.");

        _uploadsInProgress = meter.CreateUpDownCounter<long>(
            "inspections.attachment.uploads.active",
            unit: "{upload}",
            description: "Attachment uploads currently streaming to Blob Storage.");
    }

    public void RecordCreated(string outcome, string source) =>
        _recordsCreated.Add(1, new TagList
        {
            { "inspection.outcome", outcome },
            { "inspection.source", source }
        });

    public void AttachmentStored(string kind, long sizeBytes) =>
        _attachmentSize.Record(sizeBytes, new KeyValuePair<string, object?>("attachment.kind", kind));

    public void UploadFailed(string kind, string errorType) =>
        _uploadsFailed.Add(1, new TagList
        {
            { "attachment.kind", kind },
            { "error.type", errorType }
        });

    public void UploadStarted() => _uploadsInProgress.Add(1);

    public void UploadFinished() => _uploadsInProgress.Add(-1);
}
