using DatabaseMigrations.ApiModel;
using DatabaseMigrations.MigrationService;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<MigrationTelemetry>();
builder.Services.AddHostedService<ClinicDbInitializer>();

builder.Services.AddDbContextPool<ClinicDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("clinicdb"), sqlOptions =>
        sqlOptions.MigrationsAssembly(typeof(Program).Assembly)
    ));
builder.EnrichSqlServerDbContext<ClinicDbContext>();

var app = builder.Build();

app.Run();
