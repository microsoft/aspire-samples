using System.Diagnostics;
using StackExchange.Redis;

namespace HealthChecksUI.ApiService;

/// <summary>
/// Authorizes a payment: scores it for fraud, converts it to USD, asks the card processor to authorize it, and records it.
/// Fraud scoring and FX rates are non-critical (the gateway falls back to rules and cached rates); the card processor and
/// the payment store are critical, so the payment fails when either is unavailable.
/// </summary>
public sealed class PaymentGateway(
    PaymentDependencies dependencies,
    PaymentStore store,
    PaymentsTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<PaymentGateway> logger)
{
    private const int FraudDeclineThreshold = 85;

    // The FX feed's last published mid-market rates (to USD), used when the feed is unavailable
    private static readonly Dictionary<Currency, decimal> s_cachedFxRates = new()
    {
        [Currency.USD] = 1m,
        [Currency.EUR] = 1.0842m,
        [Currency.GBP] = 1.2715m,
        [Currency.CAD] = 0.7318m,
        [Currency.JPY] = 0.006694m
    };

    private static readonly string[] s_issuerDeclineReasons = ["insufficient_funds", "insufficient_funds", "do_not_honor", "expired_card"];

    public async Task<Payment> AuthorizeAsync(PaymentRequest request, Merchant merchant, CancellationToken cancellationToken)
    {
        var paymentId = $"pay_{Guid.NewGuid().ToString("N")[..16]}";
        var start = Stopwatch.GetTimestamp();

        using var activity = PaymentsTelemetry.ActivitySource.StartActivity("authorize payment");
        activity?.SetTag("contoso.payment.id", paymentId);
        activity?.SetTag("contoso.merchant.id", merchant.Id);
        activity?.SetTag("contoso.payment.amount", (double)request.Amount);
        activity?.SetTag("contoso.payment.currency", request.Currency.ToString());
        activity?.SetTag("contoso.payment.card_brand", request.CardBrand.ToString());

        telemetry.AuthorizationStarted();
        var outcome = "failed";
        var settlementAmount = 0m;

        try
        {
            List<string> fallbacks = [];

            var riskScore = await ScoreFraudAsync(paymentId, request, fallbacks, cancellationToken);
            activity?.AddEvent(new ActivityEvent("fraud.scored", tags: new() { ["contoso.payment.risk_score"] = riskScore }));

            var fxRate = await GetFxRateAsync(paymentId, request.Currency, fallbacks, cancellationToken);
            settlementAmount = Math.Round(request.Amount * fxRate, 2);
            activity?.SetTag("contoso.payment.settlement_amount_usd", (double)settlementAmount);

            var (authorizationCode, declineReason) = riskScore >= FraudDeclineThreshold
                ? (null, "suspected_fraud")
                : await AuthorizeWithCardNetworkAsync(cancellationToken);

            var status = declineReason is null ? PaymentStatus.Approved : PaymentStatus.Declined;
            var elapsed = Stopwatch.GetElapsedTime(start);
            var payment = new Payment(
                paymentId, merchant.Id, merchant.Name, request.Amount, request.Currency, request.CardBrand,
                status, authorizationCode, declineReason, settlementAmount, fxRate, riskScore, fallbacks,
                timeProvider.GetUtcNow(), Math.Round(elapsed.TotalMilliseconds, 1));

            try
            {
                await store.SaveAsync(payment);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                throw new DependencyUnavailableException(dependencies.PaymentStore, "The payment store (Redis) is unreachable.", ex);
            }

            outcome = status == PaymentStatus.Approved ? "approved" : "declined";
            activity?.SetTag("contoso.payment.outcome", outcome);

            if (status == PaymentStatus.Approved)
            {
                activity?.AddEvent(new ActivityEvent("payment.approved", tags: new() { ["contoso.payment.authorization_code"] = authorizationCode }));
                logger.PaymentApproved(paymentId, merchant.Id, request.Amount, request.Currency, request.CardBrand, settlementAmount, elapsed.TotalMilliseconds);
            }
            else
            {
                activity?.SetTag("contoso.payment.decline_reason", declineReason);
                activity?.AddEvent(new ActivityEvent("payment.declined", tags: new() { ["contoso.payment.decline_reason"] = declineReason }));
                logger.PaymentDeclined(paymentId, merchant.Id, declineReason!, riskScore);
            }

            return payment;
        }
        catch (DependencyUnavailableException ex)
        {
            activity?.SetTag("contoso.payment.outcome", outcome);
            activity?.SetTag("contoso.payment.failed_dependency", ex.Dependency.Name);
            activity?.SetTag("error.type", nameof(DependencyUnavailableException));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.PaymentFailed(ex, paymentId, merchant.Id, ex.Dependency.Name);
            throw;
        }
        finally
        {
            telemetry.AuthorizationFinished(outcome, request.CardBrand, request.Currency, Stopwatch.GetElapsedTime(start), settlementAmount);
        }
    }

    private async Task<int> ScoreFraudAsync(string paymentId, PaymentRequest request, List<string> fallbacks, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync(dependencies.FraudScoring, "score", cancellationToken);

            // Most payments are low risk; a few look like card testing or account takeover
            return Random.Shared.NextDouble() < 0.04 ? Random.Shared.Next(FraudDeclineThreshold, 100) : Random.Shared.Next(2, 60);
        }
        catch (DependencyUnavailableException)
        {
            fallbacks.Add("fraud_rules");
            Activity.Current?.AddEvent(new ActivityEvent("fraud.fallback"));
            logger.FallbackUsed(paymentId, "fraud_rules", dependencies.FraudScoring.Name);

            // Conservative static rules: large and foreign-currency payments score higher
            return (request.Amount > 1_000 ? 60 : 25) + (request.Currency == Currency.USD ? 0 : 10);
        }
    }

    private async Task<decimal> GetFxRateAsync(string paymentId, Currency currency, List<string> fallbacks, CancellationToken cancellationToken)
    {
        if (currency == Currency.USD)
        {
            return 1m;
        }

        try
        {
            await CallAsync(dependencies.FxRates, "quote", cancellationToken);

            // A live quote moves a little around the last published rate
            var drift = 1m + (decimal)(Random.Shared.NextDouble() - 0.5) * 0.004m;
            return Math.Round(s_cachedFxRates[currency] * drift, 6);
        }
        catch (DependencyUnavailableException)
        {
            fallbacks.Add("cached_fx_rate");
            Activity.Current?.AddEvent(new ActivityEvent("fx.rate.cached", tags: new() { ["contoso.payment.currency"] = currency.ToString() }));
            logger.FallbackUsed(paymentId, "cached_fx_rate", dependencies.FxRates.Name);
            return s_cachedFxRates[currency];
        }
    }

    private async Task<(string? AuthorizationCode, string? DeclineReason)> AuthorizeWithCardNetworkAsync(CancellationToken cancellationToken)
    {
        await CallAsync(dependencies.CardProcessor, "authorize", cancellationToken);

        return Random.Shared.NextDouble() < 0.06
            ? (null, s_issuerDeclineReasons[Random.Shared.Next(s_issuerDeclineReasons.Length)])
            : (Convert.ToHexString(Guid.NewGuid().ToByteArray())[..6], null);
    }

    private async Task CallAsync(SimulatedDependency dependency, string operation, CancellationToken cancellationToken)
    {
        using var activity = PaymentsTelemetry.ActivitySource.StartActivity($"{dependency.Name} {operation}", ActivityKind.Client);
        activity?.SetTag("peer.service", dependency.Name);
        activity?.SetTag("contoso.dependency.provider", dependency.Provider);

        var start = Stopwatch.GetTimestamp();
        try
        {
            await dependency.CallAsync(cancellationToken);
            telemetry.DependencyCalled(dependency.Name, succeeded: true, Stopwatch.GetElapsedTime(start));
        }
        catch (DependencyUnavailableException ex)
        {
            telemetry.DependencyCalled(dependency.Name, succeeded: false, Stopwatch.GetElapsedTime(start));
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
