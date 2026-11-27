using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DatabaseContainers.ApiService;

/// <summary>
/// Places and reads orders. Placing an order touches all three databases: the customer is verified in MySQL,
/// the lines are priced and checked against stock in the PostgreSQL catalog, and the order is saved to SQL Server.
/// </summary>
public sealed class OrderService(
    NpgsqlConnection catalogDb,
    MySqlConnection customersDb,
    SqlConnection ordersDb,
    OrderTelemetry telemetry,
    ILogger<OrderService> logger)
{
    public const int MaxLines = 10;
    public const int MaxQuantity = 10;

    public async Task<Results<Created<OrderDetails>, ValidationProblem, ProblemHttpResult>> PlaceOrderAsync(
        PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        // JSON deserialization doesn't enforce non-nullable properties, so a request without lines arrives as null
        IReadOnlyList<PlaceOrderLine> requestedLines = request.Lines ?? [];

        using var activity = OrderTelemetry.ActivitySource.StartActivity("place order");
        activity?.SetTag("outfitters.customer.id", request.CustomerId);
        activity?.SetTag("outfitters.order.line_count", requestedLines.Count);

        telemetry.PlacementStarted();
        try
        {
            if (Validate(request.CustomerId, requestedLines) is { } errors)
            {
                RecordRejection(activity, request.CustomerId, "invalid_request", errors.First().Value[0]);
                return TypedResults.ValidationProblem(errors, title: "Invalid order");
            }

            var customer = await VerifyCustomerAsync(request.CustomerId, cancellationToken);
            if (customer is null)
            {
                return Reject(activity, request.CustomerId, "customer_not_found", StatusCodes.Status404NotFound,
                    "Customer not found", $"No customer exists with ID {request.CustomerId}.");
            }

            activity?.SetTag("outfitters.customer.tier", customer.LoyaltyTier.ToString());
            activity?.AddEvent(new("customer.verified"));

            var products = await PriceLinesAsync(requestedLines, cancellationToken);

            if (requestedLines.FirstOrDefault(line => !products.ContainsKey(line.ProductId)) is { } unknown)
            {
                return Reject(activity, customer.Id, "product_not_found", StatusCodes.Status404NotFound,
                    "Product not found", $"No product exists with ID {unknown.ProductId}.");
            }

            if (requestedLines.FirstOrDefault(line => products[line.ProductId].UnitsInStock < line.Quantity) is { } shortLine)
            {
                var product = products[shortLine.ProductId];
                return Reject(activity, customer.Id, "insufficient_stock", StatusCodes.Status409Conflict,
                    "Not enough stock",
                    product.UnitsInStock == 0
                        ? $"{product.Name} is out of stock."
                        : $"Only {product.UnitsInStock} units of {product.Name} are in stock, but {shortLine.Quantity} were ordered.");
            }

            var lines = requestedLines
                .Select(line => new OrderLine(line.ProductId, products[line.ProductId].Name, line.Quantity, products[line.ProductId].Price))
                .ToList();
            var itemCount = lines.Sum(line => line.Quantity);
            var total = lines.Sum(line => line.LineTotal);

            activity?.SetTag("outfitters.order.item_count", itemCount);
            activity?.SetTag("outfitters.order.total", (double)total);
            activity?.AddEvent(new("order.priced"));

            var (orderId, placedAt) = await SaveOrderAsync(customer.Id, lines, total, cancellationToken);

            activity?.SetTag("outfitters.order.id", orderId);
            activity?.SetTag("outfitters.order.outcome", "placed");
            activity?.AddEvent(new("order.saved"));

            telemetry.OrderPlaced(customer.LoyaltyTier, total);
            logger.OrderPlaced(orderId, customer.LoyaltyTier, customer.Id, itemCount, total);

            return TypedResults.Created($"/orders/{orderId}",
                new OrderDetails(orderId, placedAt, OrderStatus.Placed, customer.Id, customer, lines, itemCount, total));
        }
        finally
        {
            telemetry.PlacementFinished();
        }
    }

    public async Task<OrderDetails?> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        if (await ordersDb.FindOrderAsync(orderId, cancellationToken) is not { } found)
        {
            return null;
        }

        var (order, lines) = found;
        var customer = await customersDb.FindCustomerAsync(order.CustomerId, cancellationToken);
        return new OrderDetails(order.Id, order.PlacedAt, order.Status, order.CustomerId, customer, lines, order.ItemCount, order.Total);
    }

    private async Task<Customer?> VerifyCustomerAsync(int customerId, CancellationToken cancellationToken)
    {
        using var activity = OrderTelemetry.ActivitySource.StartActivity("verify customer");
        activity?.SetTag("outfitters.customer.id", customerId);

        var customer = await customersDb.FindCustomerAsync(customerId, cancellationToken);
        activity?.SetTag("outfitters.customer.found", customer is not null);
        return customer;
    }

    private async Task<Dictionary<int, Product>> PriceLinesAsync(IReadOnlyList<PlaceOrderLine> lines, CancellationToken cancellationToken)
    {
        using var activity = OrderTelemetry.ActivitySource.StartActivity("price order lines");
        activity?.SetTag("outfitters.order.line_count", lines.Count);

        var products = await catalogDb.FindProductsAsync([.. lines.Select(line => line.ProductId)], cancellationToken);
        activity?.SetTag("outfitters.catalog.products_found", products.Count);
        return products;
    }

    private async Task<(int Id, DateTimeOffset PlacedAt)> SaveOrderAsync(
        int customerId, IReadOnlyList<OrderLine> lines, decimal total, CancellationToken cancellationToken)
    {
        using var activity = OrderTelemetry.ActivitySource.StartActivity("save order");
        activity?.SetTag("outfitters.order.line_count", lines.Count);

        var order = await ordersDb.InsertOrderAsync(customerId, lines, total, cancellationToken);
        activity?.SetTag("outfitters.order.id", order.Id);
        return order;
    }

    private static Dictionary<string, string[]>? Validate(int customerId, IReadOnlyList<PlaceOrderLine> lines)
    {
        var errors = new Dictionary<string, string[]>();

        if (customerId < 1)
        {
            errors["customerId"] = ["A customer ID is required."];
        }

        if (lines.Count == 0)
        {
            errors["lines"] = ["An order needs at least one line."];
        }
        else if (lines.Count > MaxLines)
        {
            errors["lines"] = [$"An order can have at most {MaxLines} lines."];
        }
        else if (lines.Any(line => line.Quantity is < 1 or > MaxQuantity))
        {
            errors["lines"] = [$"Each quantity must be between 1 and {MaxQuantity}."];
        }
        else if (lines.DistinctBy(line => line.ProductId).Count() != lines.Count)
        {
            errors["lines"] = ["Each product can appear only once per order."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private ProblemHttpResult Reject(Activity? activity, int customerId, string reason, int statusCode, string title, string detail)
    {
        RecordRejection(activity, customerId, reason, detail);
        return TypedResults.Problem(title: title, detail: detail, statusCode: statusCode);
    }

    private void RecordRejection(Activity? activity, int customerId, string reason, string detail)
    {
        activity?.SetTag("outfitters.order.outcome", "rejected");
        activity?.SetTag("outfitters.order.rejection_reason", reason);
        activity?.AddEvent(new("order.rejected"));

        telemetry.OrderRejected(reason);
        logger.OrderRejected(customerId, reason, detail);
    }
}
