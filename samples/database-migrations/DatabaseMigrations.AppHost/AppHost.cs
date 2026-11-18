var builder = DistributedApplication.CreateBuilder(args);

var sqlserver = builder.AddSqlServer("sqlserver")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var db1 = sqlserver.AddDatabase("db1");

var migrationService = builder.AddDotnetProject("migration", "../DatabaseMigrations.MigrationService/DatabaseMigrations.MigrationService.csproj")
    .WithReference(db1)
    .WaitFor(db1);

builder.AddDotnetProject("api", "../DatabaseMigrations.ApiService/DatabaseMigrations.ApiService.csproj")
    .WithReference(db1)
    .WaitForCompletion(migrationService);

builder.Build().Run();
