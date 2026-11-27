using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace HealthChecksUI.Web.Components;

/// <summary>Labels, icons, and formatting shared by the status and payments pages.</summary>
public static class StatusDisplay
{
    private static readonly CultureInfo s_usCulture = CultureInfo.GetCultureInfo("en-US");

    public static string Label(ComponentStatus status) => status switch
    {
        ComponentStatus.Degraded => "Degraded",
        ComponentStatus.Outage => "Outage",
        _ => "Operational"
    };

    /// <summary>The <c>data-state</c> value the stylesheets color by.</summary>
    public static string State(ComponentStatus status) => status switch
    {
        ComponentStatus.Degraded => "warn",
        ComponentStatus.Outage => "down",
        _ => "ok"
    };

    public static MarkupString Icon(ComponentStatus status) => status switch
    {
        ComponentStatus.Degraded => Icons.CircleAlert,
        ComponentStatus.Outage => Icons.CircleX,
        _ => Icons.CircleCheck
    };

    public static MarkupString ComponentIcon(string name) => name switch
    {
        "card-processor" => Icons.CreditCard,
        "fraud-scoring" => Icons.ShieldCheck,
        "fx-rates" => Icons.ArrowLeftRight,
        "payment-store" => Icons.Database,
        _ => Icons.Server
    };

    public static string Usd(decimal amount) => amount.ToString("C0", s_usCulture);

    public static string Money(decimal amount, string currency) => currency switch
    {
        "USD" => amount.ToString("C2", s_usCulture),
        "EUR" => $"€{amount.ToString("N2", s_usCulture)}",
        "GBP" => $"£{amount.ToString("N2", s_usCulture)}",
        "CAD" => $"CA${amount.ToString("N2", s_usCulture)}",
        "JPY" => $"¥{amount.ToString("N0", s_usCulture)}",
        _ => $"{amount.ToString("N2", s_usCulture)} {currency}"
    };

    public static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    public static string Humanize(string code) => code.Replace('_', ' ');

    public static string FallbackLabel(string fallback) => fallback switch
    {
        "cached_fx_rate" => "Cached FX rate",
        "fraud_rules" => "Fraud rules",
        _ => Humanize(fallback)
    };
}
