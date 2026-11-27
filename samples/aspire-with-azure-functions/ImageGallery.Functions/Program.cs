using ImageGallery.Functions;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddAzureQueueServiceClient("queues");
builder.AddAzureBlobServiceClient("blobs");

// The Azure SDK logs every storage request and response at Information level
builder.Logging.AddFilter("Azure.Core", LogLevel.Warning);

builder.Services.AddSingleton<PhotoProcessorTelemetry>();

builder.ConfigureFunctionsWebApplication();

builder.Build().Run();
