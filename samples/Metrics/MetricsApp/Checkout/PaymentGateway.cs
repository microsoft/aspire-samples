using System.Diagnostics;

namespace MetricsApp.Checkout;

/// <summary>
/// Simulates the payment provider: each payment method has its own latency and decline rate. The "Simulate card
/// issuer outage" dashboard command makes card payments slow and unreliable for a while, so the checkout latency
/// and decline-rate panels have an incident to show.
/// </summary>
public sealed class PaymentGateway(TimeProvider timeProvider, ILogger<PaymentGateway> logger)
{
    public static readonly TimeSpan OutageDuration = TimeSpan.FromMinutes(2);

    private long _cardOutageEndsAtTicks;

    public DateTimeOffset? CardOutageEndsAt
    {
        get
        {
            var endsAt = new DateTimeOffset(Interlocked.Read(ref _cardOutageEndsAtTicks), TimeSpan.Zero);
            return endsAt > timeProvider.GetUtcNow() ? endsAt : null;
        }
    }

    public DateTimeOffset StartCardIssuerOutage()
    {
        var endsAt = timeProvider.GetUtcNow() + OutageDuration;
        Interlocked.Exchange(ref _cardOutageEndsAtTicks, endsAt.UtcTicks);
        logger.CardIssuerOutageStarted(endsAt);
        return endsAt;
    }

    public async Task<PaymentResult> AuthorizeAsync(PaymentMethod method, decimal amount, CancellationToken cancellationToken)
    {
        using var activity = CheckoutTelemetry.ActivitySource.StartActivity("authorize payment");
        activity?.SetTag("checkout.payment.method", method.ToTagValue());
        activity?.SetTag("checkout.payment.amount", (double)amount);
        activity?.SetTag("checkout.payment.currency", "USD");

        var outage = method == PaymentMethod.Card && CardOutageEndsAt is not null;
        activity?.SetTag("checkout.payment.issuer_degraded", outage);

        var (minLatency, maxLatency, declineRate) = method switch
        {
            PaymentMethod.Card when outage => (1200, 3200, 0.35),
            PaymentMethod.Card => (150, 450, 0.03),
            PaymentMethod.PayPal => (350, 900, 0.02),
            _ => (120, 320, 0.01)
        };

        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(minLatency, maxLatency)), timeProvider, cancellationToken);

        if (Random.Shared.NextDouble() < declineRate)
        {
            var reason = outage ? "issuer_unavailable" : method == PaymentMethod.PayPal ? "do_not_honor" : "insufficient_funds";
            activity?.SetTag("checkout.payment.outcome", "declined");
            activity?.SetTag("checkout.payment.decline_reason", reason);

            // A provider outage is a fault worth flagging in the trace; a customer's declined card isn't
            if (outage)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Card issuer unavailable");
            }

            return new PaymentResult(Approved: false, AuthorizationCode: null, DeclineReason: reason);
        }

        var authorizationCode = $"AUTH-{Random.Shared.Next(100000, 999999)}";
        activity?.SetTag("checkout.payment.outcome", "approved");
        activity?.SetTag("checkout.payment.authorization_code", authorizationCode);
        return new PaymentResult(Approved: true, authorizationCode, DeclineReason: null);
    }
}

public readonly record struct PaymentResult(bool Approved, string? AuthorizationCode, string? DeclineReason);
