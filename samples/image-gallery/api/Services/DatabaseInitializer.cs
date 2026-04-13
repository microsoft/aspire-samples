using Microsoft.EntityFrameworkCore;
using Api.Data;

namespace Api.Services;

public class DatabaseInitializer(IServiceProvider serviceProvider, ILogger<DatabaseInitializer> logger) : IHostedService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<DatabaseInitializer> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImageDbContext>();

        _logger.LogInformation("============================================================");
        _logger.LogInformation("Starting database initialization...");
        _logger.LogInformation("============================================================");

        try
        {
            _logger.LogInformation("[1/2] Ensuring database is created...");
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _logger.LogInformation("  ✓ Database ready");

            _logger.LogInformation("[2/2] Applying migrations...");
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("  ✓ Migrations applied");

            _logger.LogInformation("============================================================");
            _logger.LogInformation("✓ Database initialization completed successfully");
            _logger.LogInformation("============================================================");
        }
        catch (Exception ex)
        {
            _logger.LogError("============================================================");
            _logger.LogError(ex, "✗ Database initialization failed");
            _logger.LogError("============================================================");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
