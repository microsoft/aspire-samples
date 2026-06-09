using Azure.Provisioning.Storage;

// The developer certificate APIs used below to serve the Azure Functions host over HTTPS are experimental.
#pragma warning disable ASPIRECERTIFICATES001

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("env");

var storage = builder.AddAzureStorage("storage").RunAsEmulator()
    .ConfigureInfrastructure((infrastructure) =>
    {
        var storageAccount = infrastructure.GetProvisionableResources().OfType<StorageAccount>().FirstOrDefault(r => r.BicepIdentifier == "storage")
            ?? throw new InvalidOperationException($"Could not find configured storage account with name 'storage'");

        // Ensure that public access to blobs is disabled
        storageAccount.AllowBlobPublicAccess = false;
    })
    .WithUrls(c =>
    {
        // None of the URLs are usable in the browser so hide them from the summary page
        foreach (var url in c.Urls)
        {
            url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
        }
    });
var blobs = storage.AddBlobs("blobs");
var queues = storage.AddQueues("queues");

var functions = builder.AddAzureFunctionsProject<Projects.ImageGallery_Functions>("functions")
                       .WithReference(queues)
                       .WithReference(blobs)
                       .WaitFor(storage)
                       .WithRoleAssignments(storage,
                            // Storage Account Contributor and Storage Blob Data Owner roles are required by the Azure Functions host
                            StorageBuiltInRole.StorageAccountContributor, StorageBuiltInRole.StorageBlobDataOwner,
                            // Queue Data Contributor role is required to send messages to the queue
                            StorageBuiltInRole.StorageQueueDataContributor)
                       .WithHostStorage(storage)
                       .WithUrlForEndpoint("https", u => u.DisplayText = "Functions App");

if (builder.ExecutionContext.IsRunMode)
{
    // The Functions project's launchSettings.json passes '--useHttps' to the local Azure Functions host.
    // Azure Functions Core Tools can't auto-generate a certificate on the .NET build of the tools, so the
    // trusted ASP.NET Core developer certificate is exported to a password-protected PFX and handed to
    // 'func host start' via its --cert/--password options. The PFX must be encrypted with a non-empty
    // password, so an ephemeral one is generated for local development.
    var functionsCertPassword = builder.AddParameter(
        "functions-cert-password", () => Guid.NewGuid().ToString("N"), secret: true);

    functions
        .WithHttpsDeveloperCertificate(functionsCertPassword)
        .WithHttpsCertificateConfiguration(context =>
        {
            context.Arguments.Add("--cert");
            context.Arguments.Add(context.PfxPath);
            context.Arguments.Add("--password");
            context.Arguments.Add(context.Password!);
            return Task.CompletedTask;
        });
}

builder.AddProject<Projects.ImageGallery_FrontEnd>("frontend")
       .WithReference(queues)
       .WithReference(blobs)
       .WaitFor(functions)
       .WithExternalHttpEndpoints()
       .WithUrlForEndpoint("https", u => u.DisplayText = "Frontend App")
       .WithUrlForEndpoint("http", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly);

builder.Build().Run();
