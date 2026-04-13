#:package Aspire.Hosting.Docker@13.2.2
#:package Aspire.Hosting.JavaScript@13.2.2
#:package Aspire.Hosting.Yarp@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

var frontend = builder.AddViteApp("frontend", "./frontend");

builder.AddYarp("app")
       .WithConfiguration(c =>
       {
           if (builder.ExecutionContext.IsRunMode)
           {
               // In run mode, forward all requests to vite dev server
               c.AddRoute("{**catch-all}", frontend);
           }
       })
       .WithExternalHttpEndpoints()
       .PublishWithStaticFiles(frontend);

builder.Build().Run();


