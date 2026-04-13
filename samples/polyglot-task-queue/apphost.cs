#pragma warning disable ASPIRECSHARPAPPS001

#:package Aspire.Hosting.RabbitMQ@13.2.2
#:package Aspire.Hosting.Python@13.2.2
#:package Aspire.Hosting.JavaScript@13.2.2
#:package Aspire.Hosting.Docker@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("dc");

// Add RabbitMQ with management UI
var rabbitmq = builder.AddRabbitMQ("messaging")
                      .WithManagementPlugin()
                      .WithLifetime(ContainerLifetime.Persistent)
                      .WithUrls(context =>
                      {
                         context.Urls[0].DisplayLocation = UrlDisplayLocation.DetailsOnly;
                         context.Urls[1].DisplayText = "RabbitMQ Management UI";
                      });

// Node.js API
var api = builder.AddNodeApp("api", "./api", scriptPath: "index.js")
                 .WithHttpEndpoint(env: "PORT")
                 .WithHttpHealthCheck("/health")
                 .WaitFor(rabbitmq)
                 .WithReference(rabbitmq);

// Vite frontend
var frontend = builder.AddViteApp("frontend", "./frontend")
                      .WithReference(api)
                      .WithUrl("", "Task Queue UI");

// Python data processing worker
builder.AddPythonApp("worker-python", "./worker-python", "main.py")
       .WithUv()
       .WaitFor(rabbitmq)
       .WithReference(rabbitmq);

// C# report generation worker

builder.AddCSharpApp("worker-csharp", "./worker-csharp")
       .WaitFor(rabbitmq)
       .WithReference(rabbitmq);

// Publish frontend as static files to API
api.PublishWithContainerFiles(frontend, "public");

builder.Build().Run();


