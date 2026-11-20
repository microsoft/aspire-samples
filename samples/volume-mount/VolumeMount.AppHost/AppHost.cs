var builder = DistributedApplication.CreateBuilder(args);

var sqlserver = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var sqlDatabase = sqlserver.AddDatabase("sqldb");

var blobs = builder.AddAzureStorage("Storage")
    // Use the Azurite storage emulator for local development
    .RunAsEmulator(emulator => emulator.WithDataVolume())
    .AddBlobs("BlobConnection");

var migration = builder.AddDotnetProject("migration", "../VolumeMount.MigrationService/VolumeMount.MigrationService.csproj")
    .WithReference(sqlDatabase)
    .WaitFor(sqlDatabase);

builder.AddDotnetProject("blazorweb", "../VolumeMount.BlazorWeb/VolumeMount.BlazorWeb.csproj")
    .WithReference(sqlDatabase)
    .WaitForCompletion(migration)
    .WithReference(blobs)
    .WaitFor(blobs);

builder.Build().Run();
