using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using MySqlConnector;
using Npgsql;

namespace DatabaseContainers.ApiService;

/// <summary>
/// Places a batch of orders for random customers and products so that traces, metrics, and structured logs populate.
/// Backs the AppHost's "Simulate orders" dashboard command.
/// </summary>
public sealed class OrderSimulator(
    IServiceScopeFactory scopeFactory,
    NpgsqlConnection catalogDb,
    MySqlConnection customersDb,
    ILogger<OrderSimulator> logger)
{
    public async Task<OrderSimulationSummary> RunAsync(int orders, CancellationToken cancellationToken)
    {
        var customers = await customersDb.GetCustomersAsync(cancellationToken);
        var products = await catalogDb.GetProductsAsync(category: null, cancellationToken);
        var trigger = Activity.Current?.Context;

        var totals = await Task.WhenAll(Enumerable.Range(0, orders)
            .Select(i => PlaceRandomOrderAsync(i, customers, products, trigger, cancellationToken)));

        var summary = new OrderSimulationSummary(
            orders,
            totals.Count(total => total is not null),
            totals.Count(total => total is null),
            totals.Sum(total => total ?? 0));

        logger.OrdersSimulated(summary.Orders, summary.Placed, summary.Rejected, summary.Revenue);
        return summary;
    }

    private async Task<decimal?> PlaceRandomOrderAsync(
        int index, List<Customer> customers, List<Product> products, ActivityContext? trigger, CancellationToken cancellationToken)
    {
        // Stagger the orders so the dashboard shows a steady stream rather than a single burst
        await Task.Delay(TimeSpan.FromMilliseconds(index * 150), cancellationToken);

        // Give each order its own trace, linked back to the request that started the simulation
        Activity.Current = null;
        using var activity = OrderTelemetry.ActivitySource.StartActivity(
            "simulated order", ActivityKind.Internal, parentContext: default,
            links: trigger is { } context ? [new ActivityLink(context)] : null);

        var customer = customers[Random.Shared.Next(customers.Count)];
        var picks = products.ToArray();
        Random.Shared.Shuffle(picks);

        var lines = picks
            .Take(Random.Shared.Next(1, 4))
            .Select(product => new PlaceOrderLine(product.Id, Random.Shared.NextDouble() switch { < 0.7 => 1, < 0.9 => 2, _ => 3 }))
            .ToList();

        // Each order gets its own scope, and so its own database connections
        await using var scope = scopeFactory.CreateAsyncScope();
        var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();
        var result = await orderService.PlaceOrderAsync(new PlaceOrderRequest(customer.Id, lines), cancellationToken);

        return result.Result is Created<OrderDetails> { Value: { } order } ? order.Total : null;
    }
}

/// <summary>The result of a simulated batch of orders.</summary>
public record OrderSimulationSummary(int Orders, int Placed, int Rejected, decimal Revenue);
