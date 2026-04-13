#:sdk Aspire.AppHost.Sdk@13.2.2
#:package Aspire.Hosting.Docker@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("env")
       .WithDashboard(db => db.WithHostPort(9003));

// Add Go API with in-memory storage
builder.AddGoApp("api", "./api")
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

builder.Build().Run();

// Go is not built-in, so we're going to add a custom integration for it
public static class GoExtensions
{
    extension(IDistributedApplicationBuilder builder)
    {
        public IResourceBuilder<GolangAppResource> AddGoApp(
            string name,
            string appDirectory,
            string entryPoint = "main.go")
        {
            var golangAppResource = new GolangAppResource(name, appDirectory);

            var resourceBuilder = builder.AddResource(golangAppResource)
                                      .WithArgs(["run", entryPoint])
                                      .PublishAsDockerFile(c =>
                                      {
                                          c.WithDockerfileBuilder(appDirectory, context =>
                                          {
                                              // Build stage
                                              context.Builder.From("golang:1.23-alpine", "builder")
                                                            .WorkDir("/app")
                                                            .Copy("go.mod", "./")
                                                            .Copy("go.sum", "./")
                                                            .Run("go mod download")
                                                            .Copy(".", "./")
                                                            .Run("CGO_ENABLED=0 GOOS=linux go build -a -installsuffix cgo -o main .");

                                              // Runtime stage
                                              context.Builder.From("alpine:latest")
                                                            .Run("apk --no-cache add ca-certificates")
                                                            .WorkDir("/root/")
                                                            .CopyFrom("builder", "/app/main", "./")
                                                            .Cmd(["./main"]);
                                          });
                                      });

            var goModPath = Path.Combine(appDirectory, "go.mod");
            if (File.Exists(goModPath))
            {
                var installerResource = builder.AddResource(new ExecutableResource($"{name}-go-mod-installer", "go", appDirectory))
                       .WithArgs(["mod", "tidy"])
                       .WithParentRelationship(resourceBuilder);

                resourceBuilder.WaitForCompletion(installerResource);
            }

            return resourceBuilder;
        }
    }
}
public class GolangAppResource(string name, string workingDirectory) :
    ExecutableResource(name, "go", workingDirectory);

