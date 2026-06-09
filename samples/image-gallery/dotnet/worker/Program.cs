using Worker.Data;
using Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Add Azure SQL with EF Core
builder.AddSqlServerDbContext<ImageDbContext>("imagedb");

// Add Azure Storage services
builder.AddAzureBlobContainerClient("images");
builder.AddAzureQueueServiceClient("queues");// Add background worker
builder.Services.AddHostedService<ThumbnailWorker>();

var host = builder.Build();
host.Run();
