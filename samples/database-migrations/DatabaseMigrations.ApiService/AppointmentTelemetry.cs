using System.Diagnostics.Metrics;

namespace DatabaseMigrations.ApiService;

/// <summary>
/// Domain metrics for appointment booking. The "ContosoClinic.*" meter names are registered by ServiceDefaults.
/// </summary>
public sealed class AppointmentTelemetry
{
    public const string Name = "ContosoClinic.Appointments";

    private readonly Counter<long> _booked;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _cancelled;
    private readonly Histogram<double> _leadTime;

    public AppointmentTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _booked = meter.CreateCounter<long>(
            "clinic.appointments.booked",
            unit: "{appointment}",
            description: "Number of appointments booked.");

        _rejected = meter.CreateCounter<long>(
            "clinic.appointments.rejected",
            unit: "{appointment}",
            description: "Number of booking requests rejected, by reason.");

        _cancelled = meter.CreateCounter<long>(
            "clinic.appointments.cancelled",
            unit: "{appointment}",
            description: "Number of appointments cancelled.");

        _leadTime = meter.CreateHistogram(
            "clinic.appointment.lead_time",
            unit: "d",
            description: "Days between booking an appointment and the visit.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.5, 1, 2, 3, 7, 14, 30, 60, 90] });
    }

    public void Booked(string specialty, TimeSpan leadTime)
    {
        var tag = new KeyValuePair<string, object?>("clinic.specialty", specialty);
        _booked.Add(1, tag);
        _leadTime.Record(leadTime.TotalDays, tag);
    }

    public void Rejected(string reason) =>
        _rejected.Add(1, new KeyValuePair<string, object?>("clinic.rejection.reason", reason));

    public void Cancelled(string specialty) =>
        _cancelled.Add(1, new KeyValuePair<string, object?>("clinic.specialty", specialty));
}
