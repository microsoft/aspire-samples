using System.ComponentModel;
using System.Diagnostics;
using DatabaseMigrations.ApiModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DatabaseMigrations.ApiService;

public static class AppointmentsApi
{
    private const int MaxResults = 100;
    private static readonly int[] s_allowedDurations = [15, 30, 45, 60, 90];

    public static IEndpointRouteBuilder MapClinicApi(this IEndpointRouteBuilder routes)
    {
        var logger = routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AppointmentsApi).FullName!);

        routes.MapGet("/api/clinicians", async (ClinicDbContext db, TimeProvider clock) =>
            {
                var now = clock.GetUtcNow();
                var clinicians = await db.Clinicians
                    .AsNoTracking()
                    .OrderBy(c => c.FullName)
                    .Select(c => new ClinicianDto(
                        c.Id,
                        c.FullName,
                        c.Specialty,
                        c.Appointments.Count(a => a.StartsAt >= now && a.Status == AppointmentStatus.Scheduled)))
                    .ToListAsync();

                return TypedResults.Ok(clinicians);
            })
            .WithTags("Clinicians")
            .WithName("ListClinicians")
            .WithSummary("List clinicians")
            .WithDescription("Returns every clinician with their specialty and the number of upcoming scheduled appointments.");

        var appointments = routes.MapGroup("/api/appointments").WithTags("Appointments");

        appointments.MapGet("/", async (
                [Description("Only return appointments with this clinician.")] int? clinicianId,
                [Description("Only return appointments in this status.")] AppointmentStatus? status,
                ClinicDbContext db,
                TimeProvider clock) =>
            {
                var now = clock.GetUtcNow();
                var query = db.Appointments.AsNoTracking().Where(a => a.StartsAt >= now);

                if (clinicianId is { } id)
                {
                    query = query.Where(a => a.ClinicianId == id);
                }

                if (status is { } s)
                {
                    query = query.Where(a => a.Status == s);
                }

                var results = await query
                    .OrderBy(a => a.StartsAt)
                    .Take(MaxResults)
                    .Select(ToDtoExpression)
                    .ToListAsync();

                Activity.Current?.SetTag("clinic.appointments.returned", results.Count);
                return TypedResults.Ok(results);
            })
            .WithName("ListUpcomingAppointments")
            .WithSummary("List upcoming appointments")
            .WithDescription($"Returns up to {MaxResults} upcoming appointments ordered by start time, optionally filtered by clinician and status.");

        appointments.MapGet("/{appointmentId:int}", async Task<Results<Ok<AppointmentDto>, ProblemHttpResult>> (
                [Description("The appointment ID.")] int appointmentId,
                ClinicDbContext db) =>
            {
                Activity.Current?.SetTag("clinic.appointment.id", appointmentId);

                var appointment = await db.Appointments
                    .AsNoTracking()
                    .Where(a => a.Id == appointmentId)
                    .Select(ToDtoExpression)
                    .FirstOrDefaultAsync();

                return appointment is null ? AppointmentNotFound(appointmentId) : TypedResults.Ok(appointment);
            })
            .WithName("GetAppointment")
            .WithSummary("Get an appointment")
            .WithDescription("Returns a single appointment with its clinician and patient.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapPost("/", async Task<Results<Created<AppointmentDto>, ValidationProblem, ProblemHttpResult>> (
                BookAppointmentRequest request,
                ClinicDbContext db,
                AppointmentTelemetry telemetry,
                TimeProvider clock) =>
            {
                var activity = Activity.Current;
                activity?.SetTag("clinic.clinician.id", request.ClinicianId);

                var now = clock.GetUtcNow();
                if (Validate(request, now) is { } errors)
                {
                    telemetry.Rejected("invalid");
                    logger.BookingRejected(request.ClinicianId, request.StartsAt, errors.First().Value[0]);
                    return TypedResults.ValidationProblem(errors, title: "Invalid booking request");
                }

                var clinician = await db.Clinicians.FindAsync(request.ClinicianId);
                if (clinician is null || !await db.Patients.AnyAsync(p => p.Id == request.PatientId))
                {
                    telemetry.Rejected("not_found");
                    var missing = clinician is null ? $"clinician {request.ClinicianId}" : $"patient {request.PatientId}";
                    logger.BookingRejected(request.ClinicianId, request.StartsAt, $"unknown {missing}");
                    return TypedResults.Problem(
                        title: "Clinician or patient not found",
                        detail: $"No {missing} exists.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                var endsAt = request.StartsAt.AddMinutes(request.DurationMinutes);
                var overlaps = await db.Appointments.AnyAsync(a =>
                    a.ClinicianId == request.ClinicianId
                    && a.Status != AppointmentStatus.Cancelled
                    && a.StartsAt < endsAt
                    && request.StartsAt < a.StartsAt.AddMinutes(a.DurationMinutes));

                if (overlaps)
                {
                    telemetry.Rejected("slot_taken");
                    activity?.AddEvent(new("clinic.appointment.slot_taken"));
                    logger.BookingRejected(request.ClinicianId, request.StartsAt, "slot already booked");
                    return TypedResults.Problem(
                        title: "Time slot unavailable",
                        detail: $"{clinician.FullName} already has an appointment between {request.StartsAt:u} and {endsAt:u}.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                var appointment = new Appointment
                {
                    ClinicianId = request.ClinicianId,
                    PatientId = request.PatientId,
                    StartsAt = request.StartsAt,
                    DurationMinutes = request.DurationMinutes,
                    Reason = request.Reason.Trim()
                };

                db.Appointments.Add(appointment);
                await db.SaveChangesAsync();

                telemetry.Booked(clinician.Specialty, request.StartsAt - now);
                activity?.SetTag("clinic.appointment.id", appointment.Id);
                activity?.AddEvent(new("clinic.appointment.booked"));
                logger.AppointmentBooked(appointment.Id, appointment.ClinicianId, appointment.PatientId, appointment.StartsAt);

                var dto = await db.Appointments.AsNoTracking().Where(a => a.Id == appointment.Id).Select(ToDtoExpression).FirstAsync();
                return TypedResults.Created($"/api/appointments/{appointment.Id}", dto);
            })
            .WithName("BookAppointment")
            .WithSummary("Book an appointment")
            .WithDescription("""
                Books a visit with a clinician. The start time must be in the future and on a 15-minute boundary, and the
                duration must be 15, 30, 45, 60, or 90 minutes. Returns `409 Conflict` when the clinician is already booked
                for any part of the requested slot.
                """)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        appointments.MapPost("/{appointmentId:int}/cancel", async Task<Results<Ok<AppointmentDto>, ProblemHttpResult>> (
                [Description("The appointment ID.")] int appointmentId,
                ClinicDbContext db,
                AppointmentTelemetry telemetry,
                TimeProvider clock) =>
            {
                Activity.Current?.SetTag("clinic.appointment.id", appointmentId);

                var appointment = await db.Appointments.Include(a => a.Clinician).FirstOrDefaultAsync(a => a.Id == appointmentId);
                if (appointment is null)
                {
                    return AppointmentNotFound(appointmentId);
                }

                if (appointment.Status is not AppointmentStatus.Scheduled)
                {
                    return TypedResults.Problem(
                        title: "Appointment can't be cancelled",
                        detail: $"Appointment {appointmentId} is {appointment.Status}; only scheduled appointments can be cancelled.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                appointment.Status = AppointmentStatus.Cancelled;
                appointment.CancelledAt = clock.GetUtcNow();
                await db.SaveChangesAsync();

                telemetry.Cancelled(appointment.Clinician.Specialty);
                logger.AppointmentCancelled(appointmentId, appointment.ClinicianId);

                return TypedResults.Ok(await db.Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(ToDtoExpression).FirstAsync());
            })
            .WithName("CancelAppointment")
            .WithSummary("Cancel an appointment")
            .WithDescription("Cancels a scheduled appointment and frees the clinician's slot. Returns `409 Conflict` if the appointment isn't scheduled.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static Dictionary<string, string[]>? Validate(BookAppointmentRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.StartsAt <= now)
        {
            errors[nameof(request.StartsAt)] = ["The appointment must start in the future."];
        }
        else if (request.StartsAt.Minute % 15 != 0 || request.StartsAt.Second != 0)
        {
            errors[nameof(request.StartsAt)] = ["Appointments start on a 15-minute boundary."];
        }

        if (!s_allowedDurations.Contains(request.DurationMinutes))
        {
            errors[nameof(request.DurationMinutes)] = [$"Duration must be one of {string.Join(", ", s_allowedDurations)} minutes."];
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 200)
        {
            errors[nameof(request.Reason)] = ["A reason of 1-200 characters is required."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static ProblemHttpResult AppointmentNotFound(int appointmentId) => TypedResults.Problem(
        title: "Appointment not found",
        detail: $"No appointment exists with ID {appointmentId}.",
        statusCode: StatusCodes.Status404NotFound);

    private static readonly System.Linq.Expressions.Expression<Func<Appointment, AppointmentDto>> ToDtoExpression = a => new AppointmentDto(
        a.Id,
        a.ClinicianId,
        a.Clinician.FullName,
        a.Clinician.Specialty,
        a.PatientId,
        a.Patient.FullName,
        a.StartsAt,
        a.DurationMinutes,
        a.Reason,
        a.Status,
        a.CancelledAt);
}

[Description("A request to book an appointment.")]
public record BookAppointmentRequest(
    [property: Description("The clinician to book with.")] int ClinicianId,
    [property: Description("The patient the visit is for.")] int PatientId,
    [property: Description("When the visit starts (ISO 8601, on a 15-minute boundary).")] DateTimeOffset StartsAt,
    [property: Description("Length of the visit in minutes: 15, 30, 45, 60, or 90.")] int DurationMinutes,
    [property: Description("Why the patient is coming in.")] string Reason);

[Description("A scheduled clinic visit.")]
public record AppointmentDto(
    [property: Description("The appointment ID.")] int Id,
    [property: Description("The clinician's ID.")] int ClinicianId,
    [property: Description("The clinician's display name.")] string ClinicianName,
    [property: Description("The clinician's specialty.")] string Specialty,
    [property: Description("The patient's ID.")] int PatientId,
    [property: Description("The patient's display name.")] string PatientName,
    [property: Description("When the visit starts.")] DateTimeOffset StartsAt,
    [property: Description("Length of the visit in minutes.")] int DurationMinutes,
    [property: Description("Why the patient is coming in.")] string Reason,
    [property: Description("The appointment's lifecycle status.")] AppointmentStatus Status,
    [property: Description("When the appointment was cancelled, if it was.")] DateTimeOffset? CancelledAt);

[Description("A clinician who sees patients.")]
public record ClinicianDto(
    [property: Description("The clinician ID.")] int Id,
    [property: Description("The clinician's display name.")] string FullName,
    [property: Description("The clinician's specialty.")] string Specialty,
    [property: Description("Number of upcoming scheduled appointments.")] int UpcomingAppointments);
