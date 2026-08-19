namespace Aspire.Hosting.ApplicationModel;

/// <summary>Represents a MailDev container resource.</summary>
/// <param name="name">The resource name.</param>
/// <param name="username">The optional SMTP username parameter.</param>
/// <param name="password">The SMTP password parameter.</param>
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
            $"Endpoint=smtp://{SmtpEndpoint.Property(EndpointProperty.HostAndPort)};Username={UsernameReference};******");

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, ReferenceExpression>> GetConnectionProperties() =>
    [
        new("Host", ReferenceExpression.Create($"{SmtpEndpoint.Property(EndpointProperty.Host)}")),
        new("Port", ReferenceExpression.Create($"{SmtpEndpoint.Property(EndpointProperty.Port)}")),
        new("Username", UsernameReference),
        new("Password", ReferenceExpression.Create($"{PasswordParameter}")),
        new("Uri", ReferenceExpression.Create($"smtp://{SmtpEndpoint.Property(EndpointProperty.HostAndPort)}")),
    ];
}