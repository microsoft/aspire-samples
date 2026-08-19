using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MailKit.Client;

internal sealed class MailKitHealthCheck(MailKitClientFactory factory) : IHealthCheck
{
    internal MailKitClientFactory Factory => factory;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await factory.GetSmtpClientAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(exception: exception);
        }
    }
}