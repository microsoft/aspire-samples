using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace HealthChecksUI.ApiService;

/// <summary>
/// Probes a third-party dependency. Slow responses report Degraded; failures report the registration's failure status,
/// which is Unhealthy for critical dependencies and Degraded for the ones the gateway can work around.
/// </summary>
internal sealed class DependencyHealthCheck(SimulatedDependency dependency) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["provider"] = dependency.Provider, ["critical"] = dependency.IsCritical };

        try
        {
            var latency = await dependency.CallAsync(cancellationToken);
            data["latencyMs"] = (int)latency.TotalMilliseconds;

            return latency > dependency.DegradedThreshold
                ? HealthCheckResult.Degraded($"{dependency.Provider} is responding slowly ({latency.TotalMilliseconds:F0} ms).", data: data)
                : HealthCheckResult.Healthy($"{dependency.Provider} responded in {latency.TotalMilliseconds:F0} ms.", data);
        }
        catch (DependencyUnavailableException ex)
        {
            // A simulated failure has no useful stack trace, so report the provider's error in the description instead
            return new HealthCheckResult(context.Registration.FailureStatus, $"{ex.Message} {dependency.ImpactWhenDown}", data: data);
        }
    }
}

/// <summary>Pings the Redis payment store.</summary>
internal sealed class PaymentStoreHealthCheck(IConnectionMultiplexer redis, PaymentDependencies dependencies) : IHealthCheck
{
    private static readonly TimeSpan s_degradedThreshold = TimeSpan.FromMilliseconds(100);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object> { ["provider"] = "Redis", ["critical"] = true };

        try
        {
            // Health probes run every few seconds. Parent the PING to an unsampled activity so it stays out of the traces.
            using var probe = new Activity("payment-store health probe").Start();
            probe.ActivityTraceFlags = ActivityTraceFlags.None;

            var latency = await redis.GetDatabase().PingAsync();
            data["latencyMs"] = (int)latency.TotalMilliseconds;

            return latency > s_degradedThreshold
                ? HealthCheckResult.Degraded($"Redis is responding slowly ({latency.TotalMilliseconds:F0} ms).", data: data)
                : HealthCheckResult.Healthy($"Redis responded in {Math.Max(1, latency.TotalMilliseconds):F0} ms.", data);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, $"Redis is unreachable. {dependencies.PaymentStore.ImpactWhenDown}", ex, data);
        }
    }
}

internal static class PaymentHealthCheckExtensions
{
    public const string DependencyTag = "dependency";

    public static IHealthChecksBuilder AddPaymentDependencyChecks(this IHealthChecksBuilder healthChecks, PaymentDependencies dependencies)
    {
        var timeout = TimeSpan.FromSeconds(3);

        foreach (var dependency in dependencies.Simulated)
        {
            healthChecks.Add(new HealthCheckRegistration(
                dependency.Name,
                new DependencyHealthCheck(dependency),
                failureStatus: dependency.IsCritical ? HealthStatus.Unhealthy : HealthStatus.Degraded,
                tags: [DependencyTag],
                timeout));
        }

        return healthChecks.AddCheck<PaymentStoreHealthCheck>(
            dependencies.PaymentStore.Name, HealthStatus.Unhealthy, [DependencyTag], timeout);
    }
}
