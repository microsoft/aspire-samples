using OpenTelemetry.Trace;
using Worker.Data;
using Worker.Services;
using Worker.Telemetry;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.SetSampler(new SkipIdlePollingSampler()));

// Add Azure SQL with EF Core
builder.AddSqlServerDbContext<ImageDbContext>("imagedb");

// Add Azure Storage services
builder.AddAzureBlobContainerClient("images");
builder.AddAzureQueueServiceClient("queues");

builder.Services.AddSingleton<ThumbnailTelemetry>();
builder.Services.AddHostedService<ThumbnailWorker>();

var host = builder.Build();
host.Run();
