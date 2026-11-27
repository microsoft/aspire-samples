using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using MetricsApp.Checkout;

namespace MetricsApp.Simulation;

/// <summary>
/// Drives the public checkout API like a crowd of shoppers: each one browses the catalog, fills a cart, and then checks
/// out, abandons the cart, or leaves it open. Backs the AppHost's "Simulate checkout traffic" command (development only),
/// so the Aspire dashboard, Prometheus, and Grafana fill up without clicking through the UI.
/// </summary>
public sealed class CheckoutTrafficSimulator(HttpClient http, ILogger<CheckoutTrafficSimulator> logger)
{
    public async Task<CheckoutTrafficSummary> RunAsync(int shoppers, CancellationToken cancellationToken)
    {
        await SignInAsync(cancellationToken);

        var trigger = Activity.Current?.Context;
        var results = await Task.WhenAll(Enumerable.Range(0, shoppers).Select(i => RunShopperAsync(i, trigger, cancellationToken)));

        var summary = new CheckoutTrafficSummary(
            shoppers,
            results.Count(r => r.Outcome == "checked_out"),
            results.Count(r => r.Outcome == "payment_declined"),
            results.Count(r => r.Outcome == "abandoned"),
            results.Count(r => r.Outcome == "left_open"),
            results.Count(r => r.Outcome == "failed"),
            results.Sum(r => r.OrderTotal));

        logger.CheckoutTrafficSimulated(summary.Shoppers, summary.OrdersPlaced, summary.Revenue, summary.PaymentsDeclined, summary.CartsAbandoned, summary.CartsLeftOpen, summary.Failed);
        return summary;
    }

    /// <summary>Resolves the app's own address, preferring plain HTTP so the simulator doesn't depend on the development certificate.</summary>
    public static Uri GetLocalAddress(IServiceProvider services)
    {
        var addresses = services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses;
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? addresses.First();

        var uri = new UriBuilder(address.Replace("://+", "://localhost").Replace("://*", "://localhost"));
        if (uri.Host is "0.0.0.0" or "[::]")
        {
            uri.Host = "localhost";
        }
        return uri.Uri;
    }

    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/login", new { email = Auth.TestUserEmail, password = Auth.TestUserPassword }, cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<AccessTokenResponse>(cancellationToken);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }

    private async Task<(string Outcome, decimal OrderTotal)> RunShopperAsync(int index, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        // Stagger arrivals so the dashboards show a stream of shoppers rather than a single burst
        await Task.Delay(TimeSpan.FromMilliseconds(index * 150), cancellationToken);

        // Give each shopper its own trace, linked back to the request that started the simulation
        Activity.Current = null;
        using var activity = CheckoutTelemetry.ActivitySource.StartActivity(
            "simulated shopper", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var shopper = $"shopper-{index + 1:00}";
        activity?.SetTag("checkout.simulation.shopper", shopper);

        try
        {
            var products = await http.GetFromJsonAsync<Product[]>("/api/catalog", cancellationToken) ?? [];

            await ThinkAsync(cancellationToken);
            using var created = await http.PostAsync("/api/carts", content: null, cancellationToken);
            created.EnsureSuccessStatusCode();
            var cart = (await created.Content.ReadFromJsonAsync<CartDto>(cancellationToken))!;

            foreach (var product in Random.Shared.GetItems(products, Random.Shared.Next(1, 4)).DistinctBy(p => p.Sku))
            {
                await ThinkAsync(cancellationToken);
                using var updated = await http.PutAsJsonAsync($"/api/carts/{cart.Id}/items/{product.Sku}", new CartItemUpdate(Random.Shared.Next(1, 3)), cancellationToken);
                updated.EnsureSuccessStatusCode();
            }

            await ThinkAsync(cancellationToken);

            var (outcome, orderTotal) = await DecideAsync(cart.Id, cancellationToken);
            activity?.SetTag("checkout.simulation.outcome", outcome);
            return (outcome, orderTotal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.SimulatedShopperFailed(ex, shopper);
            return ("failed", 0m);
        }
    }

    private async Task<(string Outcome, decimal OrderTotal)> DecideAsync(Guid cartId, CancellationToken cancellationToken)
    {
        var decision = Random.Shared.NextDouble();

        if (decision < 0.12)
        {
            using var abandoned = await http.DeleteAsync($"/api/carts/{cartId}", cancellationToken);
            abandoned.EnsureSuccessStatusCode();
            return ("abandoned", 0m);
        }

        if (decision < 0.2)
        {
            return ("left_open", 0m);
        }

        var payment = Random.Shared.NextDouble() switch
        {
            < 0.55 => PaymentMethod.Card,
            < 0.8 => PaymentMethod.PayPal,
            _ => PaymentMethod.ApplePay
        };
        var shipping = Random.Shared.NextDouble() < 0.3 ? ShippingMethod.Express : ShippingMethod.Standard;

        using var response = await http.PostAsJsonAsync($"/api/carts/{cartId}/checkout", new CheckoutRequest(payment, shipping), cancellationToken);

        if (response.StatusCode == HttpStatusCode.PaymentRequired)
        {
            // Most shoppers give up after a declined payment
            using var abandoned = await http.DeleteAsync($"/api/carts/{cartId}", cancellationToken);
            return ("payment_declined", 0m);
        }

        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderConfirmation>(cancellationToken);
        return ("checked_out", order!.Total);
    }

    private static Task ThinkAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(60, 300)), cancellationToken);
}

/// <summary>The result of a simulated checkout traffic run.</summary>
public sealed record CheckoutTrafficSummary(int Shoppers, int OrdersPlaced, int PaymentsDeclined, int CartsAbandoned, int CartsLeftOpen, int Failed, decimal Revenue);

/// <summary>The result of the "Simulate card issuer outage" command.</summary>
public sealed record CardIssuerOutage(DateTimeOffset CardOutageEndsAt);

public static class SimulationEndpoints
{
    public static IServiceCollection AddCheckoutSimulation(this IServiceCollection services)
    {
        services.AddHttpClient<CheckoutTrafficSimulator>((services, client) => client.BaseAddress = CheckoutTrafficSimulator.GetLocalAddress(services));
        return services;
    }

    // Back the AppHost's dashboard commands. Mapped in development only.
    public static IEndpointRouteBuilder MapCheckoutSimulation(this IEndpointRouteBuilder routes)
    {
        var dev = routes.MapGroup("/dev").ExcludeFromDescription();

        dev.MapPost("/simulate-checkouts", async (CheckoutTrafficSimulator simulator, CancellationToken cancellationToken) =>
            TypedResults.Ok(await simulator.RunAsync(shoppers: 24, cancellationToken)));

        dev.MapPost("/card-issuer-outage", (PaymentGateway payments) =>
            TypedResults.Ok(new CardIssuerOutage(payments.StartCardIssuerOutage())));

        return routes;
    }
}
