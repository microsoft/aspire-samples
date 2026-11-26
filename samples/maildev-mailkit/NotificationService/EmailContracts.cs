using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NotificationService;

[Description("The transactional email templates the service can send.")]
public enum EmailTemplate
{
    OrderConfirmation,
    PasswordReset,
    ShippingNotification
}

[Description("Parcel carriers with tracking-page support.")]
public enum Carrier
{
    Ups,
    [JsonStringEnumMemberName("fedex")] FedEx,
    Usps,
    Dhl
}

[Description("Sends an order confirmation after checkout completes.")]
public sealed class OrderConfirmationRequest
{
    [Description("The customer's email address.")]
    [Required, EmailAddress, StringLength(254)]
    public required string Email { get; init; }

    [Description("The customer's display name, used in the greeting.")]
    [Required, StringLength(100, MinimumLength = 1)]
    public required string CustomerName { get; init; }

    [Description("The storefront order number.")]
    [Required, RegularExpression(@"^CO-\d{5,10}$", ErrorMessage = "Order numbers look like CO-104582.")]
    public required string OrderNumber { get; init; }

    [Description("The purchased line items (1-50).")]
    [Required, MinLength(1), MaxLength(50)]
    public required IReadOnlyList<OrderLine> Items { get; init; }

    [Description("Shipping charged for the order, in USD.")]
    [Range(0, 1000)]
    public decimal ShippingCost { get; init; }

    [Description("Sales tax charged for the order, in USD.")]
    [Range(0, 10000)]
    public decimal Tax { get; init; }

    [Description("Where the order ships.")]
    [Required]
    public required ShippingAddress ShipTo { get; init; }
}

[Description("A purchased product.")]
public sealed class OrderLine
{
    [Description("The product SKU.")]
    [Required, StringLength(32, MinimumLength = 3)]
    public required string Sku { get; init; }

    [Description("The product name as shown on the storefront.")]
    [Required, StringLength(120, MinimumLength = 1)]
    public required string Name { get; init; }

    [Description("Units purchased (1-99).")]
    [Range(1, 99)]
    public int Quantity { get; init; }

    [Description("Unit price in USD.")]
    [Range(0.01, 100000)]
    public decimal UnitPrice { get; init; }
}

[Description("A postal address.")]
public sealed class ShippingAddress
{
    [Description("Street address.")]
    [Required, StringLength(120, MinimumLength = 1)]
    public required string Line1 { get; init; }

    [Description("City.")]
    [Required, StringLength(80, MinimumLength = 1)]
    public required string City { get; init; }

    [Description("State, province, or region code.")]
    [Required, StringLength(40, MinimumLength = 1)]
    public required string Region { get; init; }

    [Description("Postal or ZIP code.")]
    [Required, StringLength(16, MinimumLength = 3)]
    public required string PostalCode { get; init; }
}

[Description("Sends a single-use password reset link.")]
public sealed class PasswordResetRequest
{
    [Description("The account's email address.")]
    [Required, EmailAddress, StringLength(254)]
    public required string Email { get; init; }

    [Description("The customer's display name, used in the greeting.")]
    [Required, StringLength(100, MinimumLength = 1)]
    public required string CustomerName { get; init; }

    [Description("How long the reset link stays valid, in minutes (5-120). Defaults to 30.")]
    [Range(5, 120)]
    public int ExpiresInMinutes { get; init; } = 30;
}

[Description("Tells the customer their order has shipped.")]
public sealed class ShippingNotificationRequest
{
    [Description("The customer's email address.")]
    [Required, EmailAddress, StringLength(254)]
    public required string Email { get; init; }

    [Description("The customer's display name, used in the greeting.")]
    [Required, StringLength(100, MinimumLength = 1)]
    public required string CustomerName { get; init; }

    [Description("The storefront order number.")]
    [Required, RegularExpression(@"^CO-\d{5,10}$", ErrorMessage = "Order numbers look like CO-104582.")]
    public required string OrderNumber { get; init; }

    [Description("The carrier delivering the parcel.")]
    public Carrier Carrier { get; init; }

    [Description("The carrier's tracking number.")]
    [Required, StringLength(40, MinimumLength = 6)]
    public required string TrackingNumber { get; init; }

    [Description("The carrier's estimated delivery date.")]
    public DateOnly EstimatedDelivery { get; init; }

    [Description("Number of items in the parcel.")]
    [Range(1, 999)]
    public int ItemCount { get; init; } = 1;
}

[Description("Confirms that the mail server accepted the message for delivery.")]
public sealed record EmailReceipt(
    [property: Description("The RFC 5322 Message-ID assigned to the email.")] string MessageId,
    [property: Description("The template that was rendered.")] EmailTemplate Template,
    [property: Description("The recipient, masked so the full address never leaves the service.")] string Recipient,
    [property: Description("The email subject line.")] string Subject,
    [property: Description("When the SMTP server accepted the message.")] DateTimeOffset AcceptedAt);

[Description("A template the service can render.")]
public sealed record EmailTemplateInfo(
    [property: Description("The template identifier used in routes and telemetry.")] EmailTemplate Template,
    [property: Description("What the email is for.")] string Description,
    [property: Description("The send endpoint for the template.")] string Endpoint);

[Description("The outcome of sending the sample email batch.")]
public sealed record SampleBatchResult(
    [property: Description("Number of emails the SMTP server accepted.")] int Sent,
    [property: Description("Number of emails that failed to send.")] int Failed,
    [property: Description("Receipts for the accepted emails.")] IReadOnlyList<EmailReceipt> Receipts);
