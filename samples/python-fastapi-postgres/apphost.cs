#:package Aspire.Hosting.Python@13.2.2
#:package Aspire.Hosting.PostgreSQL@13.2.2
#:package Aspire.Hosting.Docker@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

var postgres = builder.AddPostgres("postgres")
                      .WithPgAdmin();
var db = postgres.AddDatabase("db");

builder.AddUvicornApp("api", "./api", "main:app")
       .WithExternalHttpEndpoints()
       .WaitFor(db)
       .WithReference(db)
       .WithReference(postgres);

builder.Build().Run();


