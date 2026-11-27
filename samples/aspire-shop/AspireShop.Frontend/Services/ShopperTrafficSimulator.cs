using System.Diagnostics;

namespace AspireShop.Frontend.Services;

/// <summary>
/// Simulates shoppers who browse the catalog, fill a basket, and then check out, abandon the basket, or leave it open.
/// Backs the AppHost's "Simulate shopper traffic" command so traces, metrics, and logs populate without using the UI.
/// </summary>
public sealed class ShopperTrafficSimulator(
    CatalogServiceClient catalog,
    BasketServiceClient basket,
    FrontendTelemetry telemetry,
    ILogger<ShopperTrafficSimulator> logger)
{
    public async Task<ShopperTrafficSummary> RunAsync(int shoppers, CancellationToken cancellationToken)
    {
        var trigger = Activity.Current?.Context;
        var results = await Task.WhenAll(Enumerable.Range(0, shoppers).Select(i => RunShopperAsync(i, trigger, cancellationToken)));

        var summary = new ShopperTrafficSummary(
            shoppers,
            results.Count(r => r.Outcome == "checked_out"),
            results.Count(r => r.Outcome == "abandoned"),
            results.Count(r => r.Outcome == "left_open"),
            results.Count(r => r.Outcome == "failed"),
            results.Sum(r => r.OrderTotal));

        logger.ShopperTrafficSimulated(summary.Shoppers, summary.OrdersPlaced, summary.Revenue, summary.BasketsAbandoned, summary.BasketsLeftOpen, summary.Failed);
        return summary;
    }

    private async Task<(string Outcome, decimal OrderTotal)> RunShopperAsync(int index, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        // Stagger arrivals so the dashboard shows a stream of shoppers rather than a single burst
        await Task.Delay(TimeSpan.FromMilliseconds(index * 150), cancellationToken);

        // Give each shopper its own trace, linked back to the request that started the simulation
        Activity.Current = null;
        using var activity = FrontendTelemetry.ActivitySource.StartActivity(
            "simulated shopper session", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var buyerId = $"shopper-{Guid.NewGuid().ToString("N")[..8]}";
        activity?.SetTag("aspireshop.buyer.id", buyerId);
        telemetry.ShopperSessionStarted();

        try
        {
            var page = await catalog.GetItemsAsync();
            var browsed = page?.Data.ToList() ?? [];

            if (page is { IsLastPage: false } && Random.Shared.NextDouble() < 0.6)
            {
                await ThinkAsync(cancellationToken);
                var nextPage = await catalog.GetItemsAsync(after: page.NextId);
                browsed.AddRange(nextPage?.Data ?? []);
            }

            var inStock = browsed.Where(i => i.AvailableStock > 0).ToArray();
            var itemsAdded = inStock.Length == 0 ? 0 : Random.Shared.Next(1, 5);

            for (var i = 0; i < itemsAdded; i++)
            {
                await ThinkAsync(cancellationToken);
                await basket.AddToCartAsync(buyerId, inStock[Random.Shared.Next(inStock.Length)]);
            }

            await ThinkAsync(cancellationToken);

            var (outcome, orderTotal) = ("left_open", 0m);
            var decision = Random.Shared.NextDouble();

            if (itemsAdded == 0)
            {
                outcome = "browsed";
            }
            else if (decision < 0.75)
            {
                var order = await basket.CheckoutBasketAsync(buyerId);
                (outcome, orderTotal) = order is null ? ("failed", 0m) : ("checked_out", order.Total);
            }
            else if (decision < 0.9)
            {
                await basket.DeleteBasketAsync(buyerId);
                outcome = "abandoned";
            }

            activity?.SetTag("aspireshop.shopper.outcome", outcome);
            logger.ShopperSessionFinished(buyerId, outcome, itemsAdded);
            return (outcome, orderTotal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.ShopperSessionFailed(ex, buyerId);
            return ("failed", 0m);
        }
        finally
        {
            telemetry.ShopperSessionEnded();
        }
    }

    private static Task ThinkAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(80, 400)), cancellationToken);
}

/// <summary>The result of a simulated shopper traffic run.</summary>
public record ShopperTrafficSummary(int Shoppers, int OrdersPlaced, int BasketsAbandoned, int BasketsLeftOpen, int Failed, decimal Revenue);
