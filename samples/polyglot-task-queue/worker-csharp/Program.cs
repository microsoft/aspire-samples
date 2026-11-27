using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WorkerCSharp;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// ReportWorker creates its own messaging spans, named and tagged the same way as the Python and
// Node.js workers'. The client's built-in instrumentation is off because its delivery spans start a
// new trace for every message (linked to, rather than parented by, the publisher).
builder.AddRabbitMQClient("messaging", settings => settings.DisableTracing = true);

builder.Services.AddSingleton<WorkerTelemetry>();
builder.Services.AddHostedService<ReportWorker>();

builder.Build().Run();
