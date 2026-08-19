using MailKit.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailKit.Client.Tests;

public sealed class MailKitExtensionsTests
{
    [Fact]
    public void AddMailKitClientRegistersOneFactoryPerScope()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:maildev"] = "smtp://localhost:1025";
        builder.AddMailKitClient("maildev");
        using var provider = builder.Services.BuildServiceProvider();

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<MailKitClientFactory>();
        Assert.Same(first, firstScope.ServiceProvider.GetRequiredService<MailKitClientFactory>());
        Assert.NotSame(first, secondScope.ServiceProvider.GetRequiredService<MailKitClientFactory>());
        Assert.NotNull(provider.GetRequiredService<HealthCheckService>());
    }

    [Fact]
    public void AddKeyedMailKitClientRegistersNamedFactory()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:transactional"] = "smtp://localhost:1025";
        builder.AddKeyedMailKitClient("transactional");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(
            scope.ServiceProvider.GetRequiredKeyedService<MailKitClientFactory>("transactional"));
        Assert.Null(scope.ServiceProvider.GetService<MailKitClientFactory>());
    }

    [Fact]
    public void AddMailKitClientUsesConnectionNameForHealthCheck()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:primary"] = "smtp://localhost:1025";
        builder.Configuration["ConnectionStrings:secondary"] = "smtp://localhost:1026";

        builder.AddMailKitClient("primary");
        builder.AddMailKitClient("secondary");

        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Collection(
            options.Registrations,
            registration => Assert.Equal("MailKit_primary", registration.Name),
            registration => Assert.Equal("MailKit_secondary", registration.Name));
    }

    [Fact]
    public void AddMailKitClientBindsEachHealthCheckToItsOwnConnection()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration["ConnectionStrings:primary"] = "smtp://localhost:1025";
        builder.Configuration["ConnectionStrings:secondary"] = "smtp://localhost:1026";

        builder.AddMailKitClient("primary");
        builder.AddMailKitClient("secondary");

        using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var primary = Assert.Single(registrations, registration => registration.Name == "MailKit_primary");
        var secondary = Assert.Single(registrations, registration => registration.Name == "MailKit_secondary");

        var primaryCheck = Assert.IsType<MailKitHealthCheck>(primary.Factory(provider));
        var secondaryCheck = Assert.IsType<MailKitHealthCheck>(secondary.Factory(provider));

        Assert.Equal(new Uri("smtp://localhost:1025"), primaryCheck.Factory.Endpoint);
        Assert.Equal(new Uri("smtp://localhost:1026"), secondaryCheck.Factory.Endpoint);
    }

    [Fact]
    public async Task HealthCheckReportsUnhealthyWhenEndpointMissing()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddMailKitClient("maildev");

        using var provider = builder.Services.BuildServiceProvider();
        var registration = Assert.Single(
            provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        var check = registration.Factory(provider);
        var result = await check.CheckHealthAsync(
            new HealthCheckContext { Registration = registration },
            TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public void FactoryValidationIsDeferredUntilResolution()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddMailKitClient("maildev", settings =>
        {
            settings.DisableHealthChecks = true;
            settings.DisableTracing = true;
            settings.DisableMetrics = true;
        });

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<InvalidOperationException>(
            scope.ServiceProvider.GetRequiredService<MailKitClientFactory>);
    }
}