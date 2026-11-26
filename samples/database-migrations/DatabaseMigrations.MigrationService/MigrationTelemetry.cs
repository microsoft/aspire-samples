using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DatabaseMigrations.MigrationService;

/// <summary>
/// Traces and metrics for the migration worker. The "ContosoClinic.*" names are registered by ServiceDefaults.
/// </summary>
public sealed class MigrationTelemetry
{
    public const string Name = "ContosoClinic.Migrations";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Counter<long> _migrationsApplied;
    private readonly Histogram<double> _migrationDuration;
    private readonly UpDownCounter<long> _migrationsPending;
    private readonly Counter<long> _seedRows;

    public MigrationTelemetry(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(Name);

        _migrationsApplied = meter.CreateCounter<long>(
            "clinic.migrations.applied",
            unit: "{migration}",
            description: "Number of EF Core migrations applied to the clinic database.");

        _migrationDuration = meter.CreateHistogram(
            "clinic.migration.duration",
            unit: "s",
            description: "Time taken to apply a single EF Core migration.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10] });

        _migrationsPending = meter.CreateUpDownCounter<long>(
            "clinic.migrations.pending",
            unit: "{migration}",
            description: "Migrations discovered as pending that haven't been applied yet.");

        _seedRows = meter.CreateCounter<long>(
            "clinic.seed.rows",
            unit: "{row}",
            description: "Number of reference rows inserted by the seeding step.");
    }

    public void MigrationsDiscovered(int pendingCount) => _migrationsPending.Add(pendingCount);

    public void MigrationApplied(string migrationName, TimeSpan elapsed, bool succeeded)
    {
        // Migration names are a small, fixed set defined in source, so they're safe as a metric dimension
        var tags = new TagList
        {
            { "clinic.migration.name", migrationName },
            { "clinic.migration.outcome", succeeded ? "applied" : "failed" }
        };

        _migrationDuration.Record(elapsed.TotalSeconds, tags);

        if (succeeded)
        {
            _migrationsApplied.Add(1, tags);
            _migrationsPending.Add(-1);
        }
    }

    public void RowsSeeded(string entity, int count) =>
        _seedRows.Add(count, new KeyValuePair<string, object?>("clinic.seed.entity", entity));
}
