using Azure.Provisioning.Storage;

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

// Blob-triggered function: checks each new claim photo, writes a thumbnail and queues the result
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
                       .WithUrlForEndpoint("http", u => u.DisplayText = "Functions App");

// Claim photo intake: Blazor UI and upload API that store photos in Blob Storage and read the function's results
builder.AddDotnetProject("frontend", "../ImageGallery.FrontEnd/ImageGallery.FrontEnd.csproj")
       .WithReference(queues)
       .WithReference(blobs)
       .WaitFor(functions)
       .WithExternalHttpEndpoints()
       .WithHttpHealthCheck("/health")
       .WithUrls(context =>
       {
           foreach (var url in context.Urls)
           {
               url.DisplayText = "Claim photo intake";
               if (url.Endpoint?.EndpointName == "http" && context.GetEndpoint("https") is { Exists: true })
               {
                   url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
               }
           }

           // Scalar and the OpenAPI document are mapped in Development only
           var endpoint = context.GetEndpoint("https") is { Exists: true } https ? https : context.GetEndpoint("http");
           context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
           context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint, DisplayLocation = UrlDisplayLocation.DetailsOnly });
       })
       .WithHttpCommand("/api/operations/sample-photos", "Upload sample photos", commandName: "upload-sample-photos", commandOptions: new()
       {
           Description = "Uploads eight damage photos for three open claims. Each one fires the function's blob trigger, so the intake page, traces and metrics fill up in seconds.",
           IconName = "ImageAdd",
           IsHighlighted = true,
           ResultMode = HttpCommandResultMode.Json
       })
       .WithHttpCommand("/api/operations/photos", "Delete all photos", commandName: "delete-all-photos", commandOptions: new()
       {
           Description = "Deletes every claim photo and thumbnail so the intake page starts empty.",
           ConfirmationMessage = "Delete every claim photo and thumbnail?",
           Method = HttpMethod.Delete,
           IconName = "Delete",
           ResultMode = HttpCommandResultMode.Json
       });

builder.Build().Run();
