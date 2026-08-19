using MailKit.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>Provides MailKit client registration extensions.</summary>
public static class MailKitExtensions
{
    /// <summary>Registers a scoped MailKit SMTP client factory.</summary>
    public static void AddMailKitClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<MailKitClientSettings>? configureSettings = null) =>
        AddMailKitClient(
            builder,
            MailKitClientSettings.DefaultConfigSectionName,
            connectionName,
            serviceKey: null,
            configureSettings);

    /// <summary>Registers a keyed scoped MailKit SMTP client factory.</summary>
    public static void AddKeyedMailKitClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<MailKitClientSettings>? configureSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        AddMailKitClient(
            builder,
            $"{MailKitClientSettings.DefaultConfigSectionName}:{name}",
            name,
            name,
            configureSettings);
    }

    private static void AddMailKitClient(
        IHostApplicationBuilder builder,
        string configurationSectionName,
        string connectionName,
        object? serviceKey,
        Action<MailKitClientSettings>? configureSettings)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);

        var settings = new MailKitClientSettings();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is { } connectionString)
        {
            settings.ParseConnectionString(connectionString);
        }

        configureSettings?.Invoke(settings);

        if (serviceKey is null)
        {
            builder.Services.AddScoped(_ => CreateFactory(settings));
        }
        else
        {
            builder.Services.AddKeyedScoped<MailKitClientFactory>(
                serviceKey,
                (_, _) => CreateFactory(settings));
        }

        if (!settings.DisableHealthChecks)
        {
            // Bind the check to this connection's own settings. Resolving the
            // factory from DI would return the last non-keyed registration, so
            // every non-keyed check would probe the same endpoint. Constructing
            // the factory here also never throws, letting MailKitHealthCheck
            // report Unhealthy for a missing endpoint instead of a 500.
            var healthCheckFactory = new MailKitClientFactory(settings);
            builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
                $"MailKit_{connectionName}",
                _ => new MailKitHealthCheck(healthCheckFactory),
                failureStatus: null,
                tags: []));
        }

        if (!settings.DisableTracing)
        {
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddSource(MailKit.Telemetry.SmtpClient.ActivitySourceName));
        }

        if (!settings.DisableMetrics)
        {
            MailKit.Telemetry.SmtpClient.Configure();
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics => metrics.AddMeter(MailKit.Telemetry.SmtpClient.MeterName));
        }
    }

    private static MailKitClientFactory CreateFactory(MailKitClientSettings settings)
    {
        if (settings.Endpoint is null)
        {
            throw new InvalidOperationException("The MailKit SMTP endpoint is not configured.");
        }

        return new MailKitClientFactory(settings);
    }
}