using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using System.Reflection;
using Xunit;

namespace MailDev.Hosting.Tests;

public sealed class MailDevResourceTests
{
    [Fact]
    public void MailDevHostingSurfaceIsExportedForAts()
    {
        var method = typeof(MailDevResourceBuilderExtensions).GetMethod(
            nameof(MailDevResourceBuilderExtensions.AddMailDev),
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Contains(
            method.GetCustomAttributes(),
            attribute => attribute.GetType().Name == "AspireExportAttribute");
        Assert.Contains(
            typeof(MailDevResource).GetCustomAttributes(),
            attribute => attribute.GetType().Name == "AspireExportAttribute");

        var nameParameter = Assert.Single(
            method.GetParameters(),
            parameter => parameter.Name == "name");
        Assert.Contains(
            nameParameter.GetCustomAttributes(),
            attribute => attribute.GetType().Name == "ResourceNameAttribute");
    }

    [Fact]
    public void AddMailDevConfiguresContainerAndEndpoints()
    {
        var builder = DistributedApplication.CreateBuilder();

        var maildev = builder.AddMailDev("maildev");

        var image = Assert.Single(maildev.Resource.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("docker.io", image.Registry);
        Assert.Equal("maildev/maildev", image.Image);
        Assert.Equal("2.2.1", image.Tag);

        var endpoints = maildev.Resource.Annotations.OfType<EndpointAnnotation>().ToArray();
        var http = Assert.Single(endpoints, endpoint => endpoint.Name == "http");
        var smtp = Assert.Single(endpoints, endpoint => endpoint.Name == "smtp");
        Assert.Equal(1080, http.TargetPort);
        Assert.Equal(1025, smtp.TargetPort);
    }

    [Fact]
    public void AddMailDevCreatesSecretPasswordAndDeferredConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();

        var maildev = builder.AddMailDev("maildev");

        Assert.True(maildev.Resource.PasswordParameter.Secret);
        Assert.Null(maildev.Resource.UsernameParameter);
        Assert.Equal(
            "Endpoint=smtp://{maildev.bindings.smtp.host}:{maildev.bindings.smtp.port};Username=mail-dev;******",
            maildev.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void AddMailDevUsesProvidedCredentialParameters()
    {
        var builder = DistributedApplication.CreateBuilder();
        var username = builder.AddParameter("smtp-user");
        var password = builder.AddParameter("smtp-password", secret: true);

        var maildev = builder.AddMailDev(
            "maildev",
            username: username,
            password: password);

        Assert.Same(username.Resource, maildev.Resource.UsernameParameter);
        Assert.Same(password.Resource, maildev.Resource.PasswordParameter);
        Assert.Equal(
            "Endpoint=smtp://{maildev.bindings.smtp.host}:{maildev.bindings.smtp.port};Username={smtp-user.value};******",
            maildev.Resource.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void AddMailDevIsExcludedFromManifest()
    {
        var builder = DistributedApplication.CreateBuilder();

        var maildev = builder.AddMailDev("maildev");

        var annotation = Assert.Single(
            maildev.Resource.Annotations.OfType<ManifestPublishingCallbackAnnotation>());
        Assert.Null(annotation.Callback);
    }

    [Fact]
    public void AddMailDevAddsHttpReadinessHealthCheck()
    {
        var builder = DistributedApplication.CreateBuilder();

        var maildev = builder.AddMailDev("maildev");

        Assert.NotEmpty(maildev.Resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public void AddMailDevExposesStructuredConnectionProperties()
    {
        var builder = DistributedApplication.CreateBuilder();

        var maildev = builder.AddMailDev("maildev");

        var properties = maildev.Resource.GetConnectionProperties()
            .ToDictionary(property => property.Key, property => property.Value.ValueExpression);

        Assert.Equal("{maildev.bindings.smtp.host}", properties["Host"]);
        Assert.Equal("{maildev.bindings.smtp.port}", properties["Port"]);
        Assert.Equal("mail-dev", properties["Username"]);
        Assert.Equal("{maildev-password.value}", properties["Password"]);
        Assert.Equal(
            "smtp://{maildev.bindings.smtp.host}:{maildev.bindings.smtp.port}",
            properties["Uri"]);
    }
}