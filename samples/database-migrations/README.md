# Database migrations with Entity Framework Core sample app

This sample demonstrates how to use Entity Framework Core's [migrations feature](https://learn.microsoft.com/ef/core/managing-schemas/migrations) with Aspire. It uses the appointment-booking schema for a small clinic, Contoso Clinic, which has clinicians, patients, and appointments. A dedicated worker applies the schema to SQL Server before the API is allowed to start.

The sample has three important projects:

- `DatabaseMigrations.ApiService`: a minimal API for listing clinicians, then booking, listing, and cancelling appointments. It's documented with OpenAPI and Scalar.
- `DatabaseMigrations.MigrationService`: a background worker that creates the database, applies each pending migration, seeds sample data, and then exits. It emits a span, a log entry, and metrics for every step.
- `DatabaseMigrations.ApiModel`: the EF Core `ClinicDbContext` and its entity types. The API and the migration service both use this project.

`DatabaseMigrations.ApiService` and `DatabaseMigrations.MigrationService` both reference the `clinicdb` database on a SQL Server resource. During local development, SQL Server runs in a persistent container with a data volume. The AppHost uses `WaitForCompletion(migration)`, so the API starts only after the migration worker has exited successfully.

The schema is built up by three migrations, so the migration trace has more than one step to show:

| Migration | Change |
| --- | --- |
| `InitialClinicSchema` | `Clinicians` and `Patients` tables, plus a unique index on patient email |
| `AddAppointments` | `Appointments` table with foreign keys and a `(ClinicianId, StartsAt)` index |
| `AddAppointmentStatus` | `Status` and `CancelledAt` columns, plus a `(Status, StartsAt)` index |

## Demonstrates

- How to create migrations in an Aspire solution
- How to apply migrations from a worker service before dependent services start, using `WaitForCompletion`
- How to instrument migrations and seeding with custom spans, metrics, and `[LoggerMessage]` structured logs
- How to add a dashboard command, **Reset database**, that re-runs the migration worker on demand

## Sample prerequisites

- [Aspire development environment](https://aspire.dev/get-started/prerequisites/)
- This sample is written in C# and targets .NET 10. It requires the [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later.
- The `dotnet ef` .NET tool is required. It can be installed by running the following in a terminal:

    ```shell
    dotnet tool install --global dotnet-ef --version "10.*"
    ```

    The migration service references `Microsoft.EntityFrameworkCore.Design` 10.x, which the tool needs to create migrations.

## Create migration

The `DatabaseMigrations.MigrationService` project contains the EF Core migrations. The [`dotnet ef` command-line tool](https://learn.microsoft.com/ef/core/managing-schemas/migrations/#install-the-tools) can be used to create new migrations:

1. Update the `Patient` entity in `ClinicDbContext.cs` in the `DatabaseMigrations.ApiModel` project. Add a `Phone` property:

    ```cs
    public class Patient
    {
        public int Id { get; set; }
        public required string FullName { get; set; }
        public DateOnly DateOfBirth { get; set; }
        public required string Email { get; set; }
        public string? Phone { get; set; }
        public List<Appointment> Appointments { get; } = [];
    }
    ```

2. Open a command prompt in the `DatabaseMigrations.MigrationService` directory and run the EF Core migration tool to create a migration named **AddPatientPhone**.

    ```bash
    dotnet ef migrations add AddPatientPhone
    ```

    The preceding command:

      - Runs EF Core migration command-line tool in the `DatabaseMigrations.MigrationService` directory.
        - `dotnet ef` is run in this location because it will be used as the default target project for the new migration and the tool will run the startup code in `Program.cs` to find and configure the context to be used.
      - Creates the migration named `AddPatientPhone` in the `DatabaseMigrations.MigrationService` project.

    > [!NOTE]
    > Other EF Core providers can impose different requirements for creating migrations. For example, the Npgsql provider requires a well-formed connection string to be present in the application's configuration, e.g. the `appsettings.Development.json` file, for the `DbContext` the migrations are being created for. The connection string need not be to an actual database, but it must be able to be successfully parsed. The SQL Server provider does not have this requirement.

3. View the new migration files in the `Migrations` folder of the `DatabaseMigrations.MigrationService` project. The next time the app runs, the migration worker applies `AddPatientPhone` in its own `apply migration AddPatientPhone` span.

> [!NOTE]
> To remove the unapplied migration you need to run `dotnet ef migrations remove --force`.  The `--force` switch tells the tool to avoid connecting to the database

## Run the app

If using the Aspire CLI, run `aspire run` from this directory.

If using VS Code, open this directory as a workspace and launch the `DatabaseMigrations.AppHost` project using either the Aspire or C# debuggers.

If using Visual Studio, open the solution file `DatabaseMigrations.slnx` and launch/debug the `DatabaseMigrations.AppHost` project.

If using the .NET CLI, run `dotnet run` from the `DatabaseMigrations.AppHost` directory.

When the app starts up, the `DatabaseMigrations.MigrationService` background worker runs against the SQL Server container. The migration service:

- Creates the `clinicdb` database if it doesn't exist.
- Applies each pending migration one at a time.
- Seeds 6 clinicians, 12 patients, and 30 upcoming appointments if the database is empty.
- Stops itself once it's done. The dashboard shows it as **Finished** with exit code `0`, and the `api` resource starts.

Because the SQL Server container is persistent, later runs find the schema already up to date and skip seeding. Use the **Reset database** command to see the full migration path again.

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with sqlserver, clinicdb and the api running, the migration worker stopped after finishing, and the api actions menu open on the Reset database command with its description (light theme)](./images/database-migrations-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with sqlserver, clinicdb and the api running, the migration worker stopped after finishing, and the api actions menu open on the Reset database command with its description (dark theme)](./images/database-migrations-dashboard-resources-dark.png#gh-dark-mode-only)

- **migration** starts once SQL Server is ready. It applies migrations, seeds the data, and then stops. **api** waits for it with `WaitForCompletion`, so the API never serves requests against a missing or partly migrated schema.
- On the **api** resource, run **Reset database**. After a confirmation prompt, it calls the development-only `POST /dev/reset-database` endpoint, which drops `clinicdb`, and then starts the **migration** resource again to recreate the schema and re-seed the data. The command is registered only in run mode.
- On the **api** resource, open **API reference (Scalar)** to try the endpoints, or **OpenAPI JSON** to view the raw document.

![Aspire dashboard details page for the migration resource showing State Finished with exit code 0, the console output from seeding, structured log counts and five migrate clinicdb traces (light theme)](./images/database-migrations-migration-finished-light.png#gh-light-mode-only)
![Aspire dashboard details page for the migration resource showing State Finished with exit code 0, the console output from seeding, structured log counts and five migrate clinicdb traces (dark theme)](./images/database-migrations-migration-finished-dark.png#gh-dark-mode-only)

Select the **migration** resource to see its **Finished** state, exit code, and console output, along with its latest logs and traces.

### Traces

![Aspire dashboard trace of migrate clinicdb with ensure database, an apply migration span per EF Core migration and a seed clinic data span, the AddAppointments span expanded to show its CREATE TABLE, CREATE INDEX and __EFMigrationsHistory SQL spans (light theme)](./images/database-migrations-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of migrate clinicdb with ensure database, an apply migration span per EF Core migration and a seed clinic data span, the AddAppointments span expanded to show its CREATE TABLE, CREATE INDEX and __EFMigrationsHistory SQL spans (dark theme)](./images/database-migrations-trace-dark.png#gh-dark-mode-only)

- **migration: migrate clinicdb**: the root span from the `ContosoClinic.Migrations` activity source. It carries `clinic.migrations.pending` and `clinic.migrations.already_applied`. Its children are:
  - `ensure database clinicdb`, which has `clinic.database.created` and an event when the database is created.
  - One `apply migration <Name>` span per migration, with `clinic.migration.id` and `clinic.migration.name`. Each contains the SQL spans for that migration (`CREATE TABLE`, `CREATE INDEX`, `ALTER TABLE`, and the `__EFMigrationsHistory` insert).
  - `seed clinic data`, with `clinic.seed.clinicians`, `clinic.seed.patients`, and `clinic.seed.appointments`, or `clinic.seed.skipped` when the data already exists.
- **api: POST /api/appointments**: the booking request, including the clinician and patient lookups, the overlap check, and the `INSERT`. Each SQL span shows `db.query.text`, `db.namespace`, and `db.system.name`.

![Aspire dashboard trace of POST /api/appointments with SELECT spans for the clinician, patient and overlapping appointments followed by the INSERT span, whose details show db.query.text and db.system.name microsoft.sql_server (light theme)](./images/database-migrations-api-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of POST /api/appointments with SELECT spans for the clinician, patient and overlapping appointments followed by the INSERT span, whose details show db.query.text and db.system.name microsoft.sql_server (dark theme)](./images/database-migrations-api-trace-dark.png#gh-dark-mode-only)

The SQL spans come from the SqlClient instrumentation that the Aspire SQL Server EF Core integration (`Aspire.Microsoft.EntityFrameworkCore.SqlServer`) turns on.

### Metrics

![Aspire dashboard metrics page charting clinic.migration.duration P50, P90 and P99 with exemplars for three Reset database runs (light theme)](./images/database-migrations-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting clinic.migration.duration P50, P90 and P99 with exemplars for three Reset database runs (dark theme)](./images/database-migrations-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `ContosoClinic.Migrations` | `clinic.migrations.applied` | Migrations applied, by `clinic.migration.name` |
| `ContosoClinic.Migrations` | `clinic.migration.duration` | Histogram of time to apply each migration (`s`), by name and `clinic.migration.outcome` |
| `ContosoClinic.Migrations` | `clinic.migrations.pending` | Up-down counter of migrations waiting to be applied |
| `ContosoClinic.Migrations` | `clinic.seed.rows` | Rows seeded, by `clinic.seed.entity` |
| `ContosoClinic.Appointments` | `clinic.appointments.booked` | Appointments booked, by `clinic.specialty` |
| `ContosoClinic.Appointments` | `clinic.appointments.rejected` | Bookings rejected, by `clinic.rejection.reason` |
| `ContosoClinic.Appointments` | `clinic.appointments.cancelled` | Appointments cancelled, by `clinic.specialty` |
| `ContosoClinic.Appointments` | `clinic.appointment.lead_time` | Histogram of days between booking and the visit (`d`) |

### Structured logs

![Aspire dashboard structured logs for the migration resource filtered to the ClinicDbInitializer category, with the MigrationApplied entry selected to show its MigrationId and ElapsedMilliseconds fields (light theme)](./images/database-migrations-logs-light.png#gh-light-mode-only)
![Aspire dashboard structured logs for the migration resource filtered to the ClinicDbInitializer category, with the MigrationApplied entry selected to show its MigrationId and ElapsedMilliseconds fields (dark theme)](./images/database-migrations-logs-dark.png#gh-dark-mode-only)

The migration worker and the API log through `[LoggerMessage]` source-generated methods, so each entry has an event name and typed fields that you can filter on. To hide EF Core's own command logs, add the filter `log.category contains ClinicDbInitializer`.

- **migration**: `DatabaseCreated` (`DatabaseName`, `DataSource`), `MigrationsDiscovered` (`PendingCount`, `AppliedCount`), `MigrationApplied` (`MigrationId`, `ElapsedMilliseconds`), `SchemaUpToDate`, `DataSeeded` (`ClinicianCount`, `PatientCount`, `AppointmentCount`), `SeedSkipped`, and `MigrationCompleted`.
- **api**: `AppointmentBooked`, `BookingRejected` (with `RejectionReason`), `AppointmentCancelled`, and `DatabaseDropped`.

### API reference

![Scalar API reference for the Contoso Clinic Appointments API showing the Book an appointment operation with its request body, example curl request and 201, 400, 404 and 409 responses (light theme)](./images/database-migrations-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Clinic Appointments API showing the Book an appointment operation with its request body, example curl request and 201, 400, 404 and 409 responses (dark theme)](./images/database-migrations-scalar-dark.png#gh-dark-mode-only)

The API serves its OpenAPI document at `/openapi/v1.json` and a Scalar UI at `/scalar`. Endpoints return `TypedResults`, and errors are returned as `ProblemDetails`. For example, an invalid booking returns `400` with validation errors, a missing clinician, patient, or appointment returns `404`, and a slot that's already taken returns `409`. `DatabaseMigrations.ApiService.http` has ready-made requests.
