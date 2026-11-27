using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MetricsApp.Checkout;

public static class CheckoutApi
{
    public static IEndpointRouteBuilder MapCheckoutApi(this IEndpointRouteBuilder routes)
    {
        var logger = routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CheckoutApi).FullName!);

        routes.MapGet("/catalog", () => TypedResults.Ok(Catalog.Products))
            .WithTags("Catalog")
            .WithName("ListProducts")
            .WithSummary("List products")
            .WithDescription("Returns every product in the Contoso Outfitters catalog. Add a product to a cart with its `sku`.");

        var carts = routes.MapGroup("/carts")
            .WithTags("Carts");

        carts.MapPost("/", (CartStore store) =>
            {
                var cart = store.Create();
                Activity.Current?.SetTag("checkout.cart.id", cart.Id.ToString());
                logger.CartCreated(cart.Id);
                return TypedResults.Created($"/api/carts/{cart.Id}", cart.ToDto());
            })
            .WithName("CreateCart")
            .WithSummary("Create a cart")
            .WithDescription("Creates an empty cart and returns it. Carts are anonymous; only checkout requires a signed-in customer.");

        carts.MapGet("/{cartId:guid}", Results<Ok<CartDto>, ProblemHttpResult> (
                [Description("The cart ID.")] Guid cartId,
                CartStore store) =>
                store.TryGet(cartId, out var cart) ? TypedResults.Ok(cart.ToDto()) : CartNotFound(cartId))
            .WithName("GetCart")
            .WithSummary("Get a cart")
            .WithDescription("Returns the cart's lines and subtotal.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        carts.MapPut("/{cartId:guid}/items/{sku}", Results<Ok<CartDto>, ProblemHttpResult, ValidationProblem> (
                [Description("The cart ID.")] Guid cartId,
                [Description("The product's SKU, for example `summit-45-backpack`.")] string sku,
                CartItemUpdate update,
                CartStore store,
                CheckoutTelemetry telemetry,
                TimeProvider timeProvider) =>
            {
                Activity.Current?.SetTag("checkout.cart.id", cartId.ToString());
                Activity.Current?.SetTag("checkout.product.sku", sku);

                if (update.Quantity is < 0 or > CartItemUpdate.MaxQuantity)
                {
                    return TypedResults.ValidationProblem(
                        new Dictionary<string, string[]> { ["quantity"] = [$"'quantity' must be between 0 and {CartItemUpdate.MaxQuantity}."] },
                        title: "Invalid quantity");
                }

                if (!store.TryGet(cartId, out var cart))
                {
                    return CartNotFound(cartId);
                }

                if (Catalog.Find(sku) is not { } product)
                {
                    return TypedResults.Problem(
                        title: "Product not found",
                        detail: $"No product exists with SKU '{sku}'.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                var added = cart.SetQuantity(product, update.Quantity, timeProvider.GetUtcNow());
                if (added is null)
                {
                    return CartLocked(cartId);
                }

                if (added > 0)
                {
                    telemetry.ItemsAdded(product.Category, added.Value);
                }

                var dto = cart.ToDto();
                logger.CartItemChanged(cart.Id, product.Sku, update.Quantity, dto.ItemCount);
                return TypedResults.Ok(dto);
            })
            .WithName("SetCartItem")
            .WithSummary("Set a product's quantity")
            .WithDescription("Adds the product to the cart, changes its quantity, or removes it when `quantity` is 0.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        carts.MapDelete("/{cartId:guid}", Results<NoContent, ProblemHttpResult> (
                [Description("The cart ID.")] Guid cartId,
                CartStore store) =>
            {
                if (!store.TryGet(cartId, out var cart))
                {
                    return CartNotFound(cartId);
                }

                // Locking first means a checkout that's already paying for this cart wins
                if (!cart.TryLock())
                {
                    return CartLocked(cartId);
                }

                if (!store.Remove(cartId))
                {
                    return CartNotFound(cartId);
                }

                logger.CartAbandoned(cartId, cart.ToDto().ItemCount);
                return TypedResults.NoContent();
            })
            .WithName("AbandonCart")
            .WithSummary("Abandon a cart")
            .WithDescription("Deletes a cart that won't be checked out.")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        routes.MapPost("/carts/{cartId:guid}/checkout", async Task<Results<Created<OrderConfirmation>, ProblemHttpResult>> (
                [Description("The cart ID.")] Guid cartId,
                CheckoutRequest request,
                ClaimsPrincipal user,
                CartStore store,
                CheckoutService checkout,
                CancellationToken cancellationToken) =>
            {
                if (!store.TryGet(cartId, out var cart))
                {
                    return CartNotFound(cartId);
                }

                var result = await checkout.PlaceOrderAsync(cart, request, user.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);

                return result.Status switch
                {
                    CheckoutStatus.Placed => TypedResults.Created((string?)null, result.Order),
                    CheckoutStatus.EmptyCart => TypedResults.Problem(
                        title: "Cart is empty",
                        detail: "Add at least one product to the cart before checking out.",
                        statusCode: StatusCodes.Status409Conflict),
                    CheckoutStatus.InProgress => CartLocked(cartId),
                    _ => TypedResults.Problem(
                        title: "Payment declined",
                        detail: $"The payment was declined ({result.DeclineReason}). Try another payment method.",
                        statusCode: StatusCodes.Status402PaymentRequired,
                        extensions: new Dictionary<string, object?> { ["declineReason"] = result.DeclineReason })
                };
            })
            .WithTags("Checkout")
            .WithName("PlaceOrder")
            .WithSummary("Place an order")
            .WithDescription("""
                Prices the cart (standard shipping is free from $100, express shipping is $19.95, and sales tax is 8.25%),
                authorizes the payment, and turns the cart into an order. Requires a bearer token from `POST /api/login`.
                A declined payment returns `402` and leaves the cart open so the customer can try another payment method.
                While a cart is being checked out, another checkout, change, or abandon of the same cart returns `409`.
                """)
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        routes.MapGet("/orders/overview", (OrderFeed feed) => TypedResults.Ok(feed.GetOverview()))
            .WithTags("Orders")
            .WithName("GetCheckoutOverview")
            .WithSummary("Get the checkout overview")
            .WithDescription("Returns order and revenue totals since the service started, the number of open carts, and the latest checkout attempts.");

        return routes;
    }

    private static ProblemHttpResult CartNotFound(Guid cartId) => TypedResults.Problem(
        title: "Cart not found",
        detail: $"No open cart exists with ID {cartId}. It may have been checked out or abandoned.",
        statusCode: StatusCodes.Status404NotFound);

    private static ProblemHttpResult CartLocked(Guid cartId) => TypedResults.Problem(
        title: "Cart is being checked out",
        detail: $"Cart {cartId} is being checked out or abandoned, so it can't change right now.",
        statusCode: StatusCodes.Status409Conflict);
}
