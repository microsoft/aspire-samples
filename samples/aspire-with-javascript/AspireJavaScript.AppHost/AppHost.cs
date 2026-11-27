var builder = DistributedApplication.CreateBuilder(args);

var shipmentsApi = builder.AddDotnetProject("shipmentsapi", "../AspireJavaScript.MinimalApi/AspireJavaScript.MinimalApi.csproj")
    .WithExternalHttpEndpoints()
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
    })
    .WithHttpCommand("/dev/simulate-lookups", "Simulate tracking lookups", commandName: "simulate-lookups", commandOptions: new()
    {
        Description = "Runs 10 board queries and 30 tracking lookups (including mistyped tracking numbers and shipments with exceptions) so traces, metrics, and logs populate.",
        IconName = "VehicleTruckProfile",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });

// Each front end proxies /api to the shipments API (same origin, so the browser's `traceparent` header
// reaches the API) and /otlp to the dashboard's OTLP/HTTP endpoint so browser spans join the same trace.
// OTLP/HTTP is required because browsers can't speak gRPC.
builder.AddJavaScriptApp("angular", "../AspireJavaScript.Angular", runScriptName: "start")
    .WithNpm(installCommand: "ci")
    .WithReference(shipmentsApi)
    .WaitFor(shipmentsApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Angular tracking board")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .PublishAsDockerFile();

builder.AddJavaScriptApp("react", "../AspireJavaScript.React", runScriptName: "start")
    .WithNpm(installCommand: "ci")
    .WithReference(shipmentsApi)
    .WaitFor(shipmentsApi)
    .WithEnvironment("BROWSER", "none") // Disable opening browser on npm start
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "React tracking board")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .PublishAsDockerFile();

builder.AddJavaScriptApp("vue", "../AspireJavaScript.Vue")
    .WithRunScript("start")
    .WithNpm(installCommand: "ci")
    .WithReference(shipmentsApi)
    .WaitFor(shipmentsApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Vue tracking board")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf)
    .PublishAsDockerFile();

var reactVite = builder.AddViteApp("reactvite", "../AspireJavaScript.Vite")
    .WithNpm(installCommand: "ci")
    .WithReference(shipmentsApi)
    .WaitFor(shipmentsApi)
    .WithEnvironment("BROWSER", "none")
    .WithUrlForEndpoint("http", url => url.DisplayText = "React + Vite tracking board")
    .WithOtlpExporter(OtlpProtocol.HttpProtobuf);

// Bundle the output of the Vite app into the wwwroot of the shipments API
shipmentsApi.PublishWithContainerFiles(reactVite, "./wwwroot");

builder.Build().Run();
