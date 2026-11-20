var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddDotnetProject("apiservice", "../ClientAppsIntegration.ApiService/ClientAppsIntegration.ApiService.csproj");

if (OperatingSystem.IsWindows())
{
    builder.AddDotnetProject("winformsclient", "../ClientAppsIntegration.WinForms/ClientAppsIntegration.WinForms.csproj")
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithExplicitStart()
        .ExcludeFromManifest();

    builder.AddDotnetProject("wpfclient", "../ClientAppsIntegration.WPF/ClientAppsIntegration.WPF.csproj")
        .WithReference(apiService)
        .WaitFor(apiService)
        .WithExplicitStart()
        .ExcludeFromManifest();
}

builder.Build().Run();
