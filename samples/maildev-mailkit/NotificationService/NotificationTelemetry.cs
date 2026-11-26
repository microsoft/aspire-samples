using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NotificationService;

/// <summary>
/// Domain traces and metrics for transactional email. ServiceDefaults registers the "Contoso.*"
/// meters and activity sources with OpenTelemetry. Tags are limited to the template and error type
/// so metric cardinality stays bounded; recipients and order numbers only appear on spans (masked).
/// </summary>
public sealed class NotificationTelemetry
{
    public const string Name = "Contoso.Notifications";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _emailsSent;
    private readonly Counter<long> _emailsFailed;
    private readonly Histogram<double> _sendDuration;
    private readonly Histogram<long> _messageSize;
    private readonly UpDownCounter<long> _sendsInFlight;

    public NotificationTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _emailsSent = meter.CreateCounter<long>(
            "contoso.notifications.emails.sent",
            unit: "{email}",
            description: "Emails accepted by the SMTP server, by template.");

        _emailsFailed = meter.CreateCounter<long>(
            "contoso.notifications.emails.failed",
            unit: "{email}",
            description: "Emails that could not be handed to the SMTP server, by template and error type.");

        _sendDuration = meter.CreateHistogram(
            "contoso.notifications.email.send.duration",
            unit: "s",
            description: "Time to render an email and hand it to the SMTP server.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5] });

        _messageSize = meter.CreateHistogram<long>(
            "contoso.notifications.email.size",
            unit: "By",
            description: "Size of the rendered MIME message.");

        _sendsInFlight = meter.CreateUpDownCounter<long>(
            "contoso.notifications.emails.in_flight",
            unit: "{email}",
            description: "Emails currently being sent.");
    }

    public void SendStarted() => _sendsInFlight.Add(1);

    public void Sent(EmailTemplate template, TimeSpan elapsed, long sizeBytes)
    {
        var tag = TemplateTag(template);
        _sendsInFlight.Add(-1);
        _emailsSent.Add(1, tag);
        _messageSize.Record(sizeBytes, tag);
        _sendDuration.Record(elapsed.TotalSeconds, tag, new("outcome", "sent"));
    }

    public void Failed(EmailTemplate template, TimeSpan elapsed, string errorType)
    {
        var tag = TemplateTag(template);
        _sendsInFlight.Add(-1);
        _emailsFailed.Add(1, tag, new("error.type", errorType));
        _sendDuration.Record(elapsed.TotalSeconds, tag, new("outcome", "failed"));
    }

    private static KeyValuePair<string, object?> TemplateTag(EmailTemplate template) =>
        new("email.template", template.ToSlug());
}
