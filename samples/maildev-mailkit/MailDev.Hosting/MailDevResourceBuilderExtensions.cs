using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>Provides extension methods for adding MailDev resources.</summary>
public static class MailDevResourceBuilderExtensions
{
    private const string Registry = "docker.io";
    private const string Image = "maildev/maildev";
    private const string Tag = "2.2.1";
    private const string UsernameEnvironmentVariable = "MAILDEV_INCOMING_USER";
    private const string PasswordEnvironmentVariable = "MAILDEV_INCOMING_PASS";

    /// <summary>Adds a MailDev container resource.</summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="httpPort">The optional host port for the MailDev web interface.</param>
    /// <param name="smtpPort">The optional host port for SMTP.</param>
    /// <param name="username">The optional SMTP username parameter.</param>
    /// <param name="password">The optional SMTP password parameter.</param>
    /// <returns>The MailDev resource builder.</returns>
    [AspireExport]
    public static IResourceBuilder<MailDevResource> AddMailDev(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? httpPort = null,
        int? smtpPort = null,
        IResourceBuilder<ParameterResource>? username = null,
        IResourceBuilder<ParameterResource>? password = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var passwordParameter = password?.Resource ??
            ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(
                builder, $"{name}-password");
        var resource = new MailDevResource(name, username?.Resource, passwordParameter);

        return builder.AddResource(resource)
            .WithImage(Image)
            .WithImageRegistry(Registry)
            .WithImageTag(Tag)
            .WithHttpEndpoint(
                targetPort: 1080,
                port: httpPort,
                name: MailDevResource.HttpEndpointName)
            .WithEndpoint(
                targetPort: 1025,
                port: smtpPort,
                name: MailDevResource.SmtpEndpointName)
            .WithEnvironment(context =>
            {
                context.EnvironmentVariables[UsernameEnvironmentVariable] = resource.UsernameReference;
                context.EnvironmentVariables[PasswordEnvironmentVariable] = resource.PasswordParameter;
            })
            .WithHttpHealthCheck("/healthz", endpointName: MailDevResource.HttpEndpointName)
            .ExcludeFromManifest();
    }
}