using System.Diagnostics;

namespace HealthChecksUI.ApiService;

/// <summary>
/// Sends a burst of realistic card payments through the gateway. Backs the AppHost's "Simulate payments traffic" command
/// so traces, metrics, and structured logs populate without a client app.
/// </summary>
public sealed class PaymentTrafficSimulator(PaymentGateway gateway, ILogger<PaymentTrafficSimulator> logger)
{
    private static readonly (Currency Currency, double Weight)[] s_currencies =
        [(Currency.USD, 0.6), (Currency.EUR, 0.15), (Currency.GBP, 0.1), (Currency.CAD, 0.08), (Currency.JPY, 0.07)];

    private static readonly (CardBrand Brand, double Weight)[] s_cardBrands =
        [(CardBrand.Visa, 0.52), (CardBrand.Mastercard, 0.33), (CardBrand.Amex, 0.15)];

    public async Task<TrafficSummary> RunAsync(int payments, CancellationToken cancellationToken)
    {
        var trigger = Activity.Current?.Context;
        var results = await Task.WhenAll(Enumerable.Range(0, payments).Select(i => RunPaymentAsync(i, trigger, cancellationToken)));

        var summary = new TrafficSummary(
            payments,
            results.Count(r => r?.Status == PaymentStatus.Approved),
            results.Count(r => r?.Status == PaymentStatus.Declined),
            results.Count(r => r is null),
            results.Where(r => r?.Status == PaymentStatus.Approved).Sum(r => r!.SettlementAmountUsd));

        logger.TrafficSimulated(summary.Payments, summary.Approved, summary.Declined, summary.Failed, summary.SettledUsd);
        return summary;
    }

    private async Task<Payment?> RunPaymentAsync(int index, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        // Stagger arrivals so the dashboard shows a stream of payments rather than a single burst
        await Task.Delay(TimeSpan.FromMilliseconds(index * 150 + Random.Shared.Next(0, 120)), cancellationToken);

        // Give each payment its own trace, linked back to the request that started the simulation
        Activity.Current = null;
        using var activity = PaymentsTelemetry.ActivitySource.StartActivity(
            "simulated payment", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var merchant = Merchants.All[Random.Shared.Next(Merchants.All.Count)];
        var currency = Pick(s_currencies);
        var request = new PaymentRequest(merchant.Id, RandomAmount(currency), currency, Pick(s_cardBrands));

        try
        {
            return await gateway.AuthorizeAsync(request, merchant, cancellationToken);
        }
        catch (DependencyUnavailableException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return null;
        }
    }

    private static decimal RandomAmount(Currency currency)
    {
        // Log-normal basket sizes: mostly $10-$150, with the occasional large order
        var usd = Math.Clamp(Math.Exp(2.6 + Random.Shared.NextDouble() * 2.4 + (Random.Shared.NextDouble() < 0.08 ? 1.8 : 0)), 4, 4_000);
        var amount = currency switch
        {
            Currency.EUR => usd * 0.92,
            Currency.GBP => usd * 0.79,
            Currency.CAD => usd * 1.37,
            Currency.JPY => Math.Round(usd * 149.4),
            _ => usd
        };

        return Math.Round((decimal)amount, currency == Currency.JPY ? 0 : 2);
    }

    private static T Pick<T>((T Value, double Weight)[] options)
    {
        var roll = Random.Shared.NextDouble();
        foreach (var (value, weight) in options)
        {
            if ((roll -= weight) < 0)
            {
                return value;
            }
        }

        return options[^1].Value;
    }
}

/// <summary>The result of a simulated traffic run.</summary>
public sealed record TrafficSummary(int Payments, int Approved, int Declined, int Failed, decimal SettledUsd);
