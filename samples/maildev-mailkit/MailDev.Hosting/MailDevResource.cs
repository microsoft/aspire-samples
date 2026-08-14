namespace Aspire.Hosting.ApplicationModel;

/// <summary>Represents a MailDev container resource.</summary>
[AspireExport]
public sealed class MailDevResource(
    [ResourceName] string name,
    ParameterResource? username,
    ParameterResource password)
    : ContainerResource(name), IResourceWithConnectionString
{
    internal const string HttpEndpointName = "http";
    internal const string SmtpEndpointName = "smtp";

    private const string DefaultUsername = "mail-dev";
    private EndpointReference? _smtpEndpoint;

    /// <summary>Gets the optional MailDev SMTP username parameter.</summary>
    public ParameterResource? UsernameParameter { get; } = username;

    /// <summary>Gets the MailDev SMTP password parameter.</summary>
    public ParameterResource PasswordParameter { get; } = password;

    internal ReferenceExpression UsernameReference =>
        UsernameParameter is not null
            ? ReferenceExpression.Create($"{UsernameParameter}")
            : ReferenceExpression.Create($"{DefaultUsername}");

    /// <summary>Gets the MailDev SMTP endpoint.</summary>
    public EndpointReference SmtpEndpoint =>
        _smtpEndpoint ??= new(this, SmtpEndpointName);

    /// <inheritdoc />
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create(
            $"Endpoint=smtp://{SmtpEndpoint.Property(EndpointProperty.HostAndPort)};Username={UsernameReference};Password={PasswordParameter}");
}