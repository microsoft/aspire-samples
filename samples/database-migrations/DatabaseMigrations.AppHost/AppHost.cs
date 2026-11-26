using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

var sqlserver = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var clinicDb = sqlserver.AddDatabase("clinicdb");

// Creates the database if needed, applies EF Core migrations, seeds data, then exits ("Finished" in the dashboard).
// It waits for the server rather than the database so it can recreate a dropped database (see "Reset database").
var migrationService = builder.AddDotnetProject("migration", "../DatabaseMigrations.MigrationService/DatabaseMigrations.MigrationService.csproj")
    .WithReference(clinicDb)
    .WaitFor(sqlserver);

// The API only starts once the migration worker has completed successfully
var api = builder.AddDotnetProject("api", "../DatabaseMigrations.ApiService/DatabaseMigrations.ApiService.csproj")
    .WithReference(clinicDb)
    .WaitForCompletion(migrationService)
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        // Surface the API reference (mapped in Development only) and keep the raw endpoint URLs on the details page
        foreach (var url in context.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    });

if (builder.ExecutionContext.IsRunMode)
{
    // The API drops the database (dev-only endpoint), then the command restarts the migration worker to rebuild it
    api.WithHttpCommand("/dev/reset-database", "Reset database", commandName: "reset-database", commandOptions: new()
    {
        Description = "Drops the clinic database and re-runs the migration worker, which re-applies every migration and re-seeds the sample data.",
        ConfirmationMessage = "Drop the clinic database and rebuild it from migrations? Appointments booked since the last seed will be lost.",
        IconName = "DatabaseLightning",
        IsHighlighted = true,
        GetCommandResult = async context =>
        {
            if (!context.Response.IsSuccessStatusCode)
            {
                return CommandResults.Failure($"The API couldn't drop the database ({(int)context.Response.StatusCode} {context.Response.ReasonPhrase}).");
            }

            var commands = context.Services.GetRequiredService<ResourceCommandService>();
            return await commands.ExecuteCommandAsync(migrationService.Resource, KnownResourceCommands.StartCommand, context.CancellationToken);
        }
    });
}

builder.Build().Run();
