// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using VolumeMount.BlazorWeb.Data;

namespace VolumeMount.MigrationService;

public class ApplicationDbInitializer(
    IServiceProvider serviceProvider,
    IHostEnvironment hostEnvironment,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    private readonly ActivitySource _activitySource = new(hostEnvironment.ApplicationName);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var activity = _activitySource.StartActivity(hostEnvironment.ApplicationName, ActivityKind.Client);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await RunMigrationAsync(dbContext, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        hostApplicationLifetime.StopApplication();
    }

    private static async Task RunMigrationAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        });
    }
}
