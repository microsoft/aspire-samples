#pragma warning disable ASPIREBROWSERLOGS001
#pragma warning disable ASPIREJAVASCRIPT001

var builder = DistributedApplication.CreateBuilder(args);

var weatherApi = builder.AddProject<Projects.AspireJavaScript_MinimalApi>("weatherapi")
    .WithExternalHttpEndpoints();

builder.AddJavaScriptApp("angular", "../AspireJavaScript.Angular", runScriptName: "start")
    .WithNpm(installCommand: "ci")
    .WithReference(weatherApi)
    .WaitFor(weatherApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithBrowserLogs()
    .PublishAsStaticWebsite("/api", weatherApi, options => options.OutputPath = "dist/weather/browser");

builder.AddJavaScriptApp("react", "../AspireJavaScript.React", runScriptName: "start")
    .WithNpm(installCommand: "ci")
    .WithReference(weatherApi)
    .WaitFor(weatherApi)
    .WithEnvironment("BROWSER", "none") // Disable opening browser on npm start
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithBrowserLogs()
    .PublishAsStaticWebsite("/api", weatherApi);

builder.AddJavaScriptApp("vue", "../AspireJavaScript.Vue")
    .WithRunScript("start")
    .WithNpm(installCommand: "ci")
    .WithReference(weatherApi)
    .WaitFor(weatherApi)
    .WithHttpEndpoint(env: "PORT")
    .WithExternalHttpEndpoints()
    .WithBrowserLogs()
    .PublishAsStaticWebsite("/api", weatherApi);

builder.AddViteApp("reactvite", "../AspireJavaScript.Vite")
    .WithNpm(installCommand: "ci")
    .WithReference(weatherApi)
    .WithEnvironment("BROWSER", "none")
    .WithBrowserLogs()
    .PublishAsStaticWebsite("/api", weatherApi);

builder.Build().Run();
