using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace HealthChecksUI.ApiService;

[JsonConverter(typeof(JsonStringEnumConverter<Currency>))]
[Description("ISO 4217 currency code. Payments settle in USD.")]
public enum Currency
{
    USD,
    EUR,
    GBP,
    CAD,
    JPY
}

[JsonConverter(typeof(JsonStringEnumConverter<CardBrand>))]
[Description("The card network.")]
public enum CardBrand
{
    Visa,
    Mastercard,
    Amex
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
[Description("Whether the payment was approved or declined.")]
public enum PaymentStatus
{
    Approved,
    Declined
}

[Description("A request to authorize a card payment.")]
public sealed record PaymentRequest(
    [property: Description("The merchant taking the payment, e.g. `fourth-coffee`. See `GET /api/merchants` for the list.")]
    [property: Required]
    string MerchantId,
    [property: Description("The amount in the payment currency.")]
    [property: Range(typeof(decimal), PaymentRequest.MinAmount, PaymentRequest.MaxAmount, ParseLimitsInInvariantCulture = true)]
    decimal Amount,
    [property: Description("The payment currency.")] Currency Currency,
    [property: Description("The card network.")] CardBrand CardBrand)
{
    public const string MinAmount = "0.50";
    public const string MaxAmount = "25000";
}

[Description("A payment authorized (or declined) by the gateway.")]
public sealed record Payment(
    [property: Description("The payment ID.")] string Id,
    [property: Description("The merchant that took the payment.")] string MerchantId,
    [property: Description("The merchant's display name.")] string MerchantName,
    [property: Description("The amount in the payment currency.")] decimal Amount,
    [property: Description("The payment currency.")] Currency Currency,
    [property: Description("The card network.")] CardBrand CardBrand,
    [property: Description("Whether the payment was approved or declined.")] PaymentStatus Status,
    [property: Description("The card network's authorization code, for approved payments.")] string? AuthorizationCode,
    [property: Description("Why the payment was declined: `insufficient_funds`, `do_not_honor`, `expired_card`, or `suspected_fraud`.")] string? DeclineReason,
    [property: Description("The amount converted to USD at the applied FX rate.")] decimal SettlementAmountUsd,
    [property: Description("The FX rate applied to convert the amount to USD (1 for USD payments).")] decimal FxRate,
    [property: Description("The fraud risk score from 0 (safe) to 99 (almost certainly fraud).")] int RiskScore,
    [property: Description("Fallbacks the gateway used because a non-critical dependency was unavailable: `fraud_rules` or `cached_fx_rate`.")] IReadOnlyList<string> Fallbacks,
    [property: Description("When the payment was processed.")] DateTimeOffset CreatedAt,
    [property: Description("How long the gateway took to process the payment, in milliseconds.")] double ProcessingMs);

[Description("A merchant that can take payments through the gateway.")]
public sealed record Merchant(
    [property: Description("The merchant ID to use in payment requests.")] string Id,
    [property: Description("The merchant's display name.")] string Name,
    [property: Description("The merchant's business category.")] string Category);

public static class Merchants
{
    public static IReadOnlyList<Merchant> All { get; } =
    [
        new("fourth-coffee", "Fourth Coffee", "Coffee shops"),
        new("alpine-ski-house", "Alpine Ski House", "Sporting goods"),
        new("tailspin-toys", "Tailspin Toys", "Toys & games"),
        new("wide-world-importers", "Wide World Importers", "Wholesale"),
        new("adventure-works", "Adventure Works Cycles", "Bicycles"),
        new("proseware", "Proseware", "Software")
    ];

    public static Merchant? Find(string id) => All.FirstOrDefault(m => m.Id == id);
}
