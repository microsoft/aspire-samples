#:package Aspire.Hosting.JavaScript@13.2.2
#:package Aspire.Hosting.Redis@13.2.2
#:package Aspire.Hosting.Yarp@13.2.2
#:package Aspire.Hosting.Docker@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

using Aspire.Hosting.Yarp.Transforms;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

var redis = builder.AddRedis("redis")
                   .WithRedisInsight();

var api = builder.AddNodeApp("api", "./api", scriptPath: "index.js")
                 .WithHttpEndpoint(env: "PORT")
                 .WithHttpHealthCheck("/health")
                 .WaitFor(redis)
                 .WithReference(redis);

var frontend = builder.AddViteApp("frontend", "./frontend")
                      .WithReference(api);

// Use YARP to serve frontend in dev mode and static files in publish mode
builder.AddYarp("app")
       .WithConfiguration(c =>
       {
           // Always proxy /api requests to Express backend
           c.AddRoute("api/{**catch-all}", api)
            .WithTransformPathRemovePrefix("/api");

           if (builder.ExecutionContext.IsRunMode)
           {
               // In dev mode, proxy all other requests to Vite dev server
               c.AddRoute("{**catch-all}", frontend);
           }
       })
       .WithExternalHttpEndpoints()
       .PublishWithStaticFiles(frontend)
       .WithExplicitStart();

builder.Build().Run();


