using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Services;

public class DatabaseInitializer(IServiceProvider serviceProvider, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImageDbContext>();

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

            logger.DatabaseReady();
        }
        catch (Exception ex)
        {
            logger.DatabaseInitializationFailed(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
