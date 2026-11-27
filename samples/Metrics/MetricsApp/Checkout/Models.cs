using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MetricsApp.Checkout;

[Description("How the customer pays for the order.")]
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethod>))]
public enum PaymentMethod
{
    [JsonStringEnumMemberName("card")] Card,
    [JsonStringEnumMemberName("paypal")] PayPal,
    [JsonStringEnumMemberName("apple_pay")] ApplePay
}

[Description("How the order is shipped. Standard shipping is free for orders of $100 or more.")]
[JsonConverter(typeof(JsonStringEnumConverter<ShippingMethod>))]
public enum ShippingMethod
{
    [JsonStringEnumMemberName("standard")] Standard,
    [JsonStringEnumMemberName("express")] Express
}

[Description("A product that can be added to a cart.")]
public sealed record Product(
    [property: Description("The product's SKU. Use it to add the product to a cart.")] string Sku,
    [property: Description("The product name.")] string Name,
    [property: Description("The product category.")] string Category,
    [property: Description("The unit price in USD.")] decimal Price,
    [property: Description("A short description of the product.")] string Description);

[Description("A line in a cart or order.")]
public sealed record CartLine(
    [property: Description("The product's SKU.")] string Sku,
    [property: Description("The product name.")] string Name,
    [property: Description("The unit price in USD.")] decimal UnitPrice,
    [property: Description("Number of units.")] int Quantity,
    [property: Description("Unit price times quantity, in USD.")] decimal LineTotal);

[Description("A shopping cart. Carts are anonymous; only checkout requires a signed-in customer.")]
public sealed record CartDto(
    [property: Description("The cart ID.")] Guid Id,
    [property: Description("The lines in the cart.")] IReadOnlyList<CartLine> Items,
    [property: Description("Total number of units in the cart.")] int ItemCount,
    [property: Description("Sum of the line totals, in USD, before shipping and tax.")] decimal Subtotal,
    [property: Description("When the cart was last changed.")] DateTimeOffset UpdatedAt);

[Description("The quantity to set for a product in a cart.")]
public sealed record CartItemUpdate(
    [property: Description("Number of units (0-10). 0 removes the product from the cart."), Range(0, CartItemUpdate.MaxQuantity)] int Quantity)
{
    public const int MaxQuantity = 10;
}

[Description("Payment and shipping choices for checkout.")]
public sealed record CheckoutRequest(
    [property: Description("How the customer pays.")] PaymentMethod PaymentMethod,
    [property: Description("How the order is shipped.")] ShippingMethod ShippingMethod);

[Description("A placed order.")]
public sealed record OrderConfirmation(
    [property: Description("The order number.")] string OrderId,
    [property: Description("The ordered lines.")] IReadOnlyList<CartLine> Items,
    [property: Description("Total number of units ordered.")] int ItemCount,
    [property: Description("Sum of the line totals, in USD.")] decimal Subtotal,
    [property: Description("Shipping cost, in USD.")] decimal Shipping,
    [property: Description("Sales tax, in USD.")] decimal Tax,
    [property: Description("Amount charged, in USD.")] decimal Total,
    [property: Description("How the customer paid.")] PaymentMethod PaymentMethod,
    [property: Description("How the order ships.")] ShippingMethod ShippingMethod,
    [property: Description("When the order was placed.")] DateTimeOffset PlacedAt);

[Description("A recent checkout attempt, placed or declined.")]
public sealed record RecentCheckout(
    [property: Description("The order number, or null when the payment was declined.")] string? OrderId,
    [property: Description("When checkout finished.")] DateTimeOffset At,
    [property: Description("Number of units.")] int ItemCount,
    [property: Description("Order total, in USD.")] decimal Total,
    [property: Description("How the customer paid.")] PaymentMethod PaymentMethod,
    [property: Description("How the order ships.")] ShippingMethod ShippingMethod,
    [property: Description("`completed` or `payment_declined`.")] string Outcome,
    [property: Description("Why the payment was declined, if it was.")] string? DeclineReason,
    [property: Description("Server-side checkout time, in milliseconds.")] double DurationMs);

[Description("Checkout activity since the service started.")]
public sealed record CheckoutOverview(
    [property: Description("Orders placed.")] int OrdersPlaced,
    [property: Description("Revenue from placed orders, in USD.")] decimal Revenue,
    [property: Description("Average order value, in USD.")] decimal AverageOrderValue,
    [property: Description("Checkout attempts whose payment was declined.")] int PaymentsDeclined,
    [property: Description("Carts that haven't been checked out or abandoned yet.")] int OpenCarts,
    [property: Description("When the simulated card issuer outage ends, or null when card payments are healthy.")] DateTimeOffset? CardOutageEndsAt,
    [property: Description("The most recent checkout attempts, newest first.")] IReadOnlyList<RecentCheckout> RecentCheckouts);

internal static class CheckoutTagValues
{
    public static string ToTagValue(this PaymentMethod method) => method switch
    {
        PaymentMethod.Card => "card",
        PaymentMethod.PayPal => "paypal",
        PaymentMethod.ApplePay => "apple_pay",
        _ => "other"
    };

    public static string ToTagValue(this ShippingMethod method) => method switch
    {
        ShippingMethod.Standard => "standard",
        ShippingMethod.Express => "express",
        _ => "other"
    };
}
