using MailKit.Client;
using Xunit;

namespace MailKit.Client.Tests;

public sealed class MailKitClientSettingsTests
{
    [Fact]
    public void ParseConnectionStringAcceptsUri()
    {
        var settings = new MailKitClientSettings();

        settings.ParseConnectionString("smtp://localhost:1025");

        Assert.Equal(new Uri("smtp://localhost:1025"), settings.Endpoint);
        Assert.Null(settings.Credentials);
    }

    [Fact]
    public void ParseConnectionStringAcceptsEndpointAndCredentials()
    {
        var settings = new MailKitClientSettings();

        settings.ParseConnectionString(
            "Endpoint=smtp://localhost:1025;Username=mail-dev;Password=secret");

        Assert.Equal(new Uri("smtp://localhost:1025"), settings.Endpoint);
        Assert.Equal("mail-dev", settings.Credentials?.UserName);
        Assert.Equal("secret", settings.Credentials?.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=localhost")]
    public void ParseConnectionStringRejectsMissingEndpoint(string? connectionString)
    {
        var settings = new MailKitClientSettings();

        Assert.Throws<InvalidOperationException>(
            () => settings.ParseConnectionString(connectionString));
    }
}