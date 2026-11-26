// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using VolumeMount.Data;

namespace VolumeMount.MigrationService;

public class InspectionDbInitializer(
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment,
    IHostApplicationLifetime hostApplicationLifetime,
    ILogger<InspectionDbInitializer> logger) : BackgroundService
{
    private readonly ActivitySource _activitySource = new(hostEnvironment.ApplicationName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity("Migrating database", ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<InspectionDbContext>();

            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                await dbContext.Database.MigrateAsync(cancellationToken);

                // The database lives on the SQL Server data volume, so existing rows survive container restarts
                var existing = await dbContext.Inspections.CountAsync(cancellationToken);
                activity?.SetTag("db.migrations.applied", pending.Count);
                activity?.SetTag("inspections.existing", existing);
                logger.MigrationsApplied(pending.Count, existing);
            });
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            logger.MigrationFailed(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }
}

internal static partial class Log
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Applied {MigrationCount} pending migrations; {InspectionCount} inspections already persisted on the data volume")]
    public static partial void MigrationsApplied(this ILogger logger, int migrationCount, int inspectionCount);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Database migration failed")]
    public static partial void MigrationFailed(this ILogger logger, Exception exception);
}
