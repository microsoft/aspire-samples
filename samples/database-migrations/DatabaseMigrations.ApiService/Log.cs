namespace DatabaseMigrations.ApiService;

internal static partial class Log
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Booked appointment {AppointmentId} with clinician {ClinicianId} for patient {PatientId} at {StartsAt:u}")]
    public static partial void AppointmentBooked(this ILogger logger, int appointmentId, int clinicianId, int patientId, DateTimeOffset startsAt);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "Rejected booking for clinician {ClinicianId} at {StartsAt:u}: {RejectionReason}")]
    public static partial void BookingRejected(this ILogger logger, int clinicianId, DateTimeOffset startsAt, string rejectionReason);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Cancelled appointment {AppointmentId} with clinician {ClinicianId}")]
    public static partial void AppointmentCancelled(this ILogger logger, int appointmentId, int clinicianId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "Dropped database {DatabaseName}; the migration worker will recreate and seed it")]
    public static partial void DatabaseDropped(this ILogger logger, string databaseName);
}
