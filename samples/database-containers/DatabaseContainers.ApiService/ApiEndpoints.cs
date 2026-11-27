using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;

namespace DatabaseContainers.ApiService;

public static class ApiEndpoints
{
    public const int DefaultOrderLimit = 20;
    public const int MaxOrderLimit = 100;

    public static IEndpointRouteBuilder MapProductsApi(this IEndpointRouteBuilder routes)
    {
        var logger = CreateLogger(routes);
        var products = routes.MapGroup("")
            .WithTags("Products");

        products.MapGet("/products", async (
                [Description("Only return products in this category, such as `Camping`.")] string? category,
                NpgsqlConnection db,
                CancellationToken cancellationToken) =>
                TypedResults.Ok(await db.GetProductsAsync(category, cancellationToken)))
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription("Returns the product catalog from the PostgreSQL `catalog` database, optionally filtered by category.");

        products.MapGet("/products/{productId:int}", async Task<Results<Ok<Product>, ProblemHttpResult>> (
                [Description("The product ID.")] int productId,
                NpgsqlConnection db,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("outfitters.product.id", productId);

                if (await db.FindProductAsync(productId, cancellationToken) is { } product)
                {
                    return TypedResults.Ok(product);
                }

                logger.EntityNotFound("Product", productId, "catalog");
                return NotFound("Product not found", $"No product exists with ID {productId}.");
            })
            .WithName("GetProduct")
            .WithSummary("Get a product")
            .WithDescription("Returns a single product, including its price and the units in stock, from the PostgreSQL `catalog` database.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    public static IEndpointRouteBuilder MapCustomersApi(this IEndpointRouteBuilder routes)
    {
        var logger = CreateLogger(routes);
        var customers = routes.MapGroup("")
            .WithTags("Customers");

        customers.MapGet("/customers", async (MySqlConnection db, CancellationToken cancellationToken) =>
                TypedResults.Ok(await db.GetCustomersAsync(cancellationToken)))
            .WithName("ListCustomers")
            .WithSummary("List customers")
            .WithDescription("Returns every customer account from the MySQL `customers` database.");

        customers.MapGet("/customers/{customerId:int}", async Task<Results<Ok<Customer>, ProblemHttpResult>> (
                [Description("The customer ID.")] int customerId,
                MySqlConnection db,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("outfitters.customer.id", customerId);

                if (await db.FindCustomerAsync(customerId, cancellationToken) is { } customer)
                {
                    return TypedResults.Ok(customer);
                }

                logger.EntityNotFound("Customer", customerId, "customers");
                return NotFound("Customer not found", $"No customer exists with ID {customerId}.");
            })
            .WithName("GetCustomer")
            .WithSummary("Get a customer")
            .WithDescription("Returns a single customer account, including the loyalty tier, from the MySQL `customers` database.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return routes;
    }

    public static IEndpointRouteBuilder MapOrdersApi(this IEndpointRouteBuilder routes)
    {
        var logger = CreateLogger(routes);
        var orders = routes.MapGroup("")
            .WithTags("Orders");

        orders.MapGet("/orders", async Task<Results<Ok<List<OrderSummary>>, ValidationProblem>> (
                [Description("Only return orders placed by this customer.")] int? customerId,
                SqlConnection db,
                CancellationToken cancellationToken,
                [Description("The maximum number of orders to return (1-100).")][Range(1, MaxOrderLimit)] int limit = DefaultOrderLimit) =>
            {
                if (limit is < 1 or > MaxOrderLimit)
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { [nameof(limit)] = [$"'{nameof(limit)}' must be between 1 and {MaxOrderLimit}."] },
                        title: "Invalid order query");
                }

                return TypedResults.Ok(await db.GetOrdersAsync(customerId, limit, cancellationToken));
            })
            .WithName("ListOrders")
            .WithSummary("List recent orders")
            .WithDescription("Returns the most recent orders from the SQL Server `orders` database, newest first, optionally for a single customer.");

        orders.MapGet("/orders/{orderId:int}", async Task<Results<Ok<OrderDetails>, ProblemHttpResult>> (
                [Description("The order ID.")] int orderId,
                OrderService orderService,
                CancellationToken cancellationToken) =>
            {
                Activity.Current?.SetTag("outfitters.order.id", orderId);

                if (await orderService.GetOrderAsync(orderId, cancellationToken) is { } order)
                {
                    return TypedResults.Ok(order);
                }

                logger.EntityNotFound("Order", orderId, "orders");
                return NotFound("Order not found", $"No order exists with ID {orderId}.");
            })
            .WithName("GetOrder")
            .WithSummary("Get an order")
            .WithDescription("Returns an order and its lines from the SQL Server `orders` database, together with the customer's account from the MySQL `customers` database.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/orders", (PlaceOrderRequest request, OrderService orderService, CancellationToken cancellationToken) =>
                orderService.PlaceOrderAsync(request, cancellationToken))
            .WithName("PlaceOrder")
            .WithSummary("Place an order")
            .WithDescription("""
                Places an order across all three databases. The API verifies the customer in MySQL, prices each line
                from the PostgreSQL catalog and checks it against the units in stock, and then saves the order and its
                lines to SQL Server in a transaction.

                Returns `400` for an invalid request, `404` for an unknown customer or product, and `409` when a product
                doesn't have enough stock.
                """)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return routes;
    }

    private static ILogger CreateLogger(IEndpointRouteBuilder routes) =>
        routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ApiEndpoints).FullName!);

    private static ProblemHttpResult NotFound(string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status404NotFound);
}
