namespace DatabaseMigrations.MigrationService;

internal static partial class Log
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Created database {DatabaseName} on {DataSource}")]
    public static partial void DatabaseCreated(this ILogger logger, string databaseName, string dataSource);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information,
        Message = "Found {PendingCount} pending migration(s); {AppliedCount} already applied to {DatabaseName}")]
    public static partial void MigrationsDiscovered(this ILogger logger, int pendingCount, int appliedCount, string databaseName);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Applied migration {MigrationId} in {ElapsedMilliseconds:F0} ms")]
    public static partial void MigrationApplied(this ILogger logger, string migrationId, double elapsedMilliseconds);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information,
        Message = "Database {DatabaseName} schema is up to date at {LatestMigrationId}")]
    public static partial void SchemaUpToDate(this ILogger logger, string databaseName, string? latestMigrationId);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information,
        Message = "Seeded {ClinicianCount} clinicians, {PatientCount} patients, and {AppointmentCount} appointments")]
    public static partial void DataSeeded(this ILogger logger, int clinicianCount, int patientCount, int appointmentCount);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Information,
        Message = "Skipped seeding because {ClinicianCount} clinicians already exist")]
    public static partial void SeedSkipped(this ILogger logger, int clinicianCount);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Information,
        Message = "Clinic database ready in {ElapsedMilliseconds:F0} ms; stopping migration worker")]
    public static partial void MigrationCompleted(this ILogger logger, double elapsedMilliseconds);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Error,
        Message = "Migration {MigrationId} failed")]
    public static partial void MigrationFailed(this ILogger logger, Exception exception, string migrationId);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Critical,
        Message = "Clinic database initialization failed; dependent services won't start")]
    public static partial void InitializationFailed(this ILogger logger, Exception exception);
}
