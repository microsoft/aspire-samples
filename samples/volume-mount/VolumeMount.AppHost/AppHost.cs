var builder = DistributedApplication.CreateBuilder(args);

// Inspection records live in SQL Server. The data volume keeps the database files
// (/var/opt/mssql) outside the container, so records survive restarts and container re-creation.
var sqlserver = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var sqlDatabase = sqlserver.AddDatabase("sqldb");

// Photos and documents live in Azure Blob Storage. Locally that's the Azurite emulator,
// whose data volume (/data) keeps uploaded blobs across restarts too.
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume());

var attachments = storage.AddBlobContainer("attachments", blobContainerName: "inspection-attachments");

var migration = builder.AddDotnetProject("migration", "../VolumeMount.MigrationService/VolumeMount.MigrationService.csproj")
    .WithReference(sqlDatabase)
    .WaitFor(sqlDatabase);

builder.AddDotnetProject("blazorweb", "../VolumeMount.BlazorWeb/VolumeMount.BlazorWeb.csproj")
    .WithReference(sqlDatabase)
    .WaitForCompletion(migration)
    .WithReference(attachments)
    .WaitFor(attachments)
    .WithHttpHealthCheck("/health")
    .WithUrls(context =>
    {
        foreach (var url in context.Urls)
        {
            url.DisplayText = $"Inspections app ({url.Endpoint?.EndpointName})";
        }

        var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint, DisplayLocation = UrlDisplayLocation.DetailsOnly });
    })
    .WithHttpCommand("/api/dev/seed", "Seed sample data", commandName: "seed-sample-data", commandOptions: new()
    {
        Description = "Creates a week of realistic equipment inspections: rows in SQL Server and nameplate photos and readings in Blob Storage.",
        IconName = "DatabaseArrowUp",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    })
    .WithHttpCommand("/api/storage/status", "Check persisted data", commandName: "check-persisted-data", commandOptions: new()
    {
        Description = "Reports how many inspections and attachments are stored, plus when SQL Server last started. Run it after restarting sqlserver to prove the data survived.",
        Method = HttpMethod.Get,
        IconName = "DatabaseSearch",
        ResultMode = HttpCommandResultMode.Json
    });

builder.Build().Run();
