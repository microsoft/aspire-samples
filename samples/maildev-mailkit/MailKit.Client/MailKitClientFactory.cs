using MailKit.Net.Smtp;

namespace MailKit.Client;

/// <summary>Creates and reuses a connected SMTP client for the current scope.</summary>
public sealed class MailKitClientFactory(MailKitClientSettings settings) : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private SmtpClient? _client;

    internal Uri? Endpoint => settings.Endpoint;

    /// <summary>Gets a connected and, when configured, authenticated SMTP client.</summary>
    public async Task<ISmtpClient> GetSmtpClientAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var endpoint = settings.Endpoint ??
                throw new InvalidOperationException("The MailKit SMTP endpoint is not configured.");
            var client = new SmtpClient();

            try
            {
                await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                if (settings.Credentials is not null)
                {
                    await client.AuthenticateAsync(settings.Credentials, cancellationToken)
                        .ConfigureAwait(false);
                }

                _client = client;
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Dispose();
        _semaphore.Dispose();
    }
}