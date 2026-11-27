using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public class DatabaseInitializer(IServiceProvider serviceProvider, ServiceDeskTelemetry telemetry, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceDeskDbContext>();

        try
        {
            if (db.Database.GetMigrations().Any())
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken);
            }

            // Start the open-requests up-down counter from what's already in the database.
            var open = await db.Requests
                .Where(r => r.Status != RequestStatus.Resolved)
                .GroupBy(r => r.Priority)
                .Select(g => new { Priority = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            foreach (var group in open)
            {
                telemetry.OpenRequestsChanged(group.Priority, group.Count);
            }

            logger.DatabaseReady(open.Sum(g => g.Count));
        }
        catch (Exception ex)
        {
            logger.DatabaseInitializationFailed(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}