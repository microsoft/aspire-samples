using System.Diagnostics;
using DatabaseMigrations.ApiModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace DatabaseMigrations.MigrationService;

/// <summary>
/// Creates the clinic database, applies pending EF Core migrations one at a time, seeds reference data,
/// and then stops so the AppHost can start the API (which uses WaitForCompletion on this worker).
/// </summary>
public sealed class ClinicDbInitializer(
    IServiceProvider serviceProvider,
    MigrationTelemetry telemetry,
    ILogger<ClinicDbInitializer> logger,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClinicDbContext>();
        var databaseName = dbContext.Database.GetDbConnection().Database;

        using var activity = MigrationTelemetry.ActivitySource.StartActivity($"migrate {databaseName}");
        activity?.SetTag("db.system.name", "microsoft.sql_server");
        activity?.SetTag("db.namespace", databaseName);

        try
        {
            await EnsureDatabaseAsync(dbContext, databaseName, cancellationToken);
            await ApplyMigrationsAsync(dbContext, databaseName, cancellationToken);
            await SeedAsync(dbContext, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.InitializationFailed(ex);
            throw;
        }

        logger.MigrationCompleted(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        hostApplicationLifetime.StopApplication();
    }

    private async Task EnsureDatabaseAsync(ClinicDbContext dbContext, string databaseName, CancellationToken cancellationToken)
    {
        using var activity = MigrationTelemetry.ActivitySource.StartActivity($"ensure database {databaseName}");
        activity?.SetTag("db.namespace", databaseName);

        var dbCreator = dbContext.GetService<IRelationalDatabaseCreator>();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        var created = await strategy.ExecuteAsync(async () =>
        {
            if (await dbCreator.ExistsAsync(cancellationToken))
            {
                return false;
            }

            await dbCreator.CreateAsync(cancellationToken);
            return true;
        });

        activity?.SetTag("clinic.database.created", created);

        if (created)
        {
            activity?.AddEvent(new("clinic.database.created"));
            logger.DatabaseCreated(databaseName, dbContext.Database.GetDbConnection().DataSource);
        }
    }

    private async Task ApplyMigrationsAsync(ClinicDbContext dbContext, string databaseName, CancellationToken cancellationToken)
    {
        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        telemetry.MigrationsDiscovered(pending.Count);
        logger.MigrationsDiscovered(pending.Count, applied.Count, databaseName);

        Activity.Current?.SetTag("clinic.migrations.pending", pending.Count);
        Activity.Current?.SetTag("clinic.migrations.already_applied", applied.Count);

        // Apply migrations one at a time (instead of a single MigrateAsync) so each gets its own span, log, and duration
        var migrator = dbContext.GetService<IMigrator>();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        foreach (var migrationId in pending)
        {
            var migrationName = migrationId[(migrationId.IndexOf('_') + 1)..];

            using var activity = MigrationTelemetry.ActivitySource.StartActivity($"apply migration {migrationName}");
            activity?.SetTag("db.namespace", databaseName);
            activity?.SetTag("clinic.migration.id", migrationId);
            activity?.SetTag("clinic.migration.name", migrationName);

            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await strategy.ExecuteAsync(() => migrator.MigrateAsync(migrationId, cancellationToken));
            }
            catch (Exception ex)
            {
                telemetry.MigrationApplied(migrationName, Stopwatch.GetElapsedTime(startedAt), succeeded: false);
                activity?.AddException(ex);
                activity?.SetStatus(ActivityStatusCode.Error);
                logger.MigrationFailed(ex, migrationId);
                throw;
            }

            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            telemetry.MigrationApplied(migrationName, elapsed, succeeded: true);
            activity?.AddEvent(new("clinic.migration.applied"));
            logger.MigrationApplied(migrationId, elapsed.TotalMilliseconds);
        }

        logger.SchemaUpToDate(databaseName, pending.LastOrDefault() ?? applied.LastOrDefault());
    }

    private async Task SeedAsync(ClinicDbContext dbContext, CancellationToken cancellationToken)
    {
        using var activity = MigrationTelemetry.ActivitySource.StartActivity("seed clinic data");

        var existingClinicians = await dbContext.Clinicians.CountAsync(cancellationToken);
        if (existingClinicians > 0)
        {
            activity?.SetTag("clinic.seed.skipped", true);
            activity?.AddEvent(new("clinic.seed.skipped"));
            logger.SeedSkipped(existingClinicians);
            return;
        }

        var (clinicians, patients, appointments) = ClinicSeedData.Create(DateTimeOffset.UtcNow);

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Seed atomically so a retry never leaves a half-seeded database behind
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            dbContext.Clinicians.AddRange(clinicians);
            dbContext.Patients.AddRange(patients);
            dbContext.Appointments.AddRange(appointments);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        telemetry.RowsSeeded("clinician", clinicians.Count);
        telemetry.RowsSeeded("patient", patients.Count);
        telemetry.RowsSeeded("appointment", appointments.Count);

        activity?.SetTag("clinic.seed.clinicians", clinicians.Count);
        activity?.SetTag("clinic.seed.patients", patients.Count);
        activity?.SetTag("clinic.seed.appointments", appointments.Count);
        logger.DataSeeded(clinicians.Count, patients.Count, appointments.Count);
    }
}
