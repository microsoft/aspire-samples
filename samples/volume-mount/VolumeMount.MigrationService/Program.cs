using Microsoft.AspNetCore.Identity;
using VolumeMount.BlazorWeb.Data;
using VolumeMount.MigrationService;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHostedService<ApplicationDbInitializer>();

builder.AddSqlServerDbContext<ApplicationDbContext>("sqldb");

// Match the Identity store schema version configured by the web app so the
// EF model lines up with the migrations snapshot and MigrateAsync can apply.
builder.Services.Configure<IdentityOptions>(options =>
    options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

var host = builder.Build();

host.Run();
