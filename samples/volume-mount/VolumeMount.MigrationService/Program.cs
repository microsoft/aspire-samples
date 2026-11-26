using VolumeMount.Data;
using VolumeMount.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<InspectionDbInitializer>();

builder.AddSqlServerDbContext<InspectionDbContext>("sqldb");

var host = builder.Build();

host.Run();
