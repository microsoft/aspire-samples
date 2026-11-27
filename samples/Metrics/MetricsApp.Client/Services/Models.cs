namespace MetricsApp.Client.Services;

public sealed record Product(string Sku, string Name, string Category, decimal Price, string Description);

public sealed record CartLine(string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record Cart(Guid Id, List<CartLine> Items, int ItemCount, decimal Subtotal);

public sealed record OrderConfirmation(
    string OrderId,
    List<CartLine> Items,
    int ItemCount,
    decimal Subtotal,
    decimal Shipping,
    decimal Tax,
    decimal Total,
    string PaymentMethod,
    string ShippingMethod,
    DateTimeOffset PlacedAt);

public sealed record RecentCheckout(
    string? OrderId,
    DateTimeOffset At,
    int ItemCount,
    decimal Total,
    string PaymentMethod,
    string ShippingMethod,
    string Outcome,
    string? DeclineReason,
    double DurationMs);

public sealed record CheckoutOverview(
    int OrdersPlaced,
    decimal Revenue,
    decimal AverageOrderValue,
    int PaymentsDeclined,
    int OpenCarts,
    DateTimeOffset? CardOutageEndsAt,
    List<RecentCheckout> RecentCheckouts);

public sealed record ProblemDetails(string? Title, string? Detail, int? Status);

public static class Labels
{
    private static readonly System.Globalization.CultureInfo s_usd = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    // Prices are in USD, so format them the same way regardless of the browser's culture
    public static string Money(decimal amount) => amount.ToString("C", s_usd);

    public static string Number(int value) => value.ToString("N0", s_usd);

    public static string Duration(double milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds:0} ms" : (milliseconds / 1000).ToString("0.00 s", s_usd);
    public static string Payment(string method) => method switch
    {
        "card" => "Card",
        "paypal" => "PayPal",
        "apple_pay" => "Apple Pay",
        _ => method
    };

    public static string Shipping(string method) => method switch
    {
        "standard" => "Standard",
        "express" => "Express",
        _ => method
    };

    public static string DeclineReason(string? reason) => reason switch
    {
        "issuer_unavailable" => "Issuer unavailable",
        "insufficient_funds" => "Insufficient funds",
        "do_not_honor" => "Do not honor",
        null => "Declined",
        _ => reason
    };
}
