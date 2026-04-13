#:package Aspire.Hosting.PostgreSQL@13.2.2
#:package Aspire.Hosting.JavaScript@13.2.2
#:package Aspire.Hosting.Docker@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

var postgres = builder.AddPostgres("postgres")
                      .WithDataVolume() // volume to persist data
                      .WithLifetime(ContainerLifetime.Persistent) // keep the container running between application runs
                      .WithPgAdmin(c => c.WithLifetime(ContainerLifetime.Persistent)); // optional: add pgAdmin with persistent lifetime

var db = postgres.AddDatabase("db");

var api = builder.AddCSharpApp("api", "./api")
                 .WithHttpHealthCheck("/health")
                 .WithExternalHttpEndpoints()
                 .WaitFor(db)
                 .WithReference(db)
                 .WithUrls(context =>
                 {
                     foreach (var url in context.Urls)
                     {
                         url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
                     }

                     context.Urls.Add(new()
                     {
                         Url = "/scalar",
                         DisplayText = "API Reference",
                         Endpoint = context.GetEndpoint("https")
                     });
                 })
                 .PublishAsDockerComposeService((_, svc) =>
                 {
                    // When creating the docker compose service
                    svc.Restart = "always";
                 });

var frontend = builder.AddViteApp("frontend", "./frontend")
                      .WithEndpoint("http", e => e.Port = 9081) // set a fixed port
                      .WithReference(api)
                      .WithUrl("", "Todo UI");

api.PublishWithContainerFiles(frontend, "wwwroot");

builder.Build().Run();


