using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

public static class HealthReportCheckExtensions
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Adds a resource health check that reads the overall status an ASP.NET Core health checks endpoint writes to its
    /// response body (<c>Healthy</c>, <c>Degraded</c>, or <c>Unhealthy</c>).
    /// </summary>
    /// <remarks>
    /// <c>WithHttpHealthCheck</c> only distinguishes success from failure status codes, and ASP.NET Core returns
    /// <c>200 OK</c> for both Healthy and Degraded, so a degraded service would show as Healthy in the dashboard.
    /// </remarks>
    /// <param name="builder">The resource builder.</param>
    /// <param name="path">The path of the health checks endpoint.</param>
    /// <param name="endpointName">The name of the HTTP endpoint that serves the health checks endpoint.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<T> WithHealthReportCheck<T>(this IResourceBuilder<T> builder, string path = "/health", string endpointName = "http")
        where T : IResourceWithEndpoints
    {
        var endpoint = builder.GetEndpoint(endpointName);
        var key = $"{builder.Resource.Name}_{endpointName}_health_report";

        builder.ApplicationBuilder.Services.AddHealthChecks().AddAsyncCheck(key, async cancellationToken =>
        {
            try
            {
                using var response = await s_httpClient.GetAsync(new Uri(new Uri(endpoint.Url), path), cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                return Enum.TryParse<HealthStatus>(body.Trim(), ignoreCase: true, out var status)
                    ? new HealthCheckResult(status, $"{path} reported {status}.")
                    : HealthCheckResult.Unhealthy($"{path} returned {(int)response.StatusCode} without a health status.");
            }
            catch (HttpRequestException ex)
            {
                return HealthCheckResult.Unhealthy($"{path} couldn't be reached.", ex);
            }
        });

        return builder.WithHealthCheck(key);
    }
}
