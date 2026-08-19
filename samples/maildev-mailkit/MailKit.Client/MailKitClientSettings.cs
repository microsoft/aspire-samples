using System.Data.Common;
using System.Net;

namespace MailKit.Client;

/// <summary>Provides settings for connecting a MailKit SMTP client.</summary>
public sealed class MailKitClientSettings
{
    internal const string DefaultConfigSectionName = "MailKit:Client";

    /// <summary>Gets or sets the SMTP endpoint.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets optional SMTP credentials.</summary>
    public NetworkCredential? Credentials { get; set; }

    /// <summary>Gets or sets whether health checks are disabled.</summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>Gets or sets whether tracing is disabled.</summary>
    public bool DisableTracing { get; set; }

    /// <summary>Gets or sets whether metrics are disabled.</summary>
    public bool DisableMetrics { get; set; }

    internal void ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"A connection string must be provided through ConnectionStrings:<name> or {DefaultConfigSectionName}:Endpoint.");
        }

        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            Endpoint = uri;
            return;
        }

        var values = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (!values.TryGetValue("Endpoint", out var endpoint) ||
            !Uri.TryCreate(endpoint?.ToString(), UriKind.Absolute, out uri))
        {
            throw new InvalidOperationException("The SMTP connection string must contain a valid Endpoint value.");
        }

        Endpoint = uri;
        if (values.TryGetValue("Username", out var username) &&
            values.TryGetValue("Password", out var password))
        {
            Credentials = new NetworkCredential(username?.ToString(), password?.ToString());
        }
    }
}