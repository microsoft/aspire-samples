using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using MimeKit;

namespace NotificationService;

/// <summary>
/// Renders the transactional emails. Markup uses tables and inline styles because many email
/// clients ignore &lt;style&gt; blocks and modern CSS layout.
/// </summary>
public static class EmailTemplates
{
    private const string StoreUrl = "https://contoso-outfitters.example";
    private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");

    public static readonly IReadOnlyList<EmailTemplateInfo> All =
    [
        new(EmailTemplate.OrderConfirmation, "Itemized receipt sent right after checkout.", "/api/emails/order-confirmations"),
        new(EmailTemplate.PasswordReset, "Single-use password reset link that expires after a short window.", "/api/emails/password-resets"),
        new(EmailTemplate.ShippingNotification, "Carrier, tracking link, and delivery estimate once the parcel ships.", "/api/emails/shipping-notifications"),
    ];

    public static RenderedEmail OrderConfirmation(OrderConfirmationRequest request, MailboxAddress to)
    {
        var subtotal = request.Items.Sum(i => i.UnitPrice * i.Quantity);
        var total = subtotal + request.ShippingCost + request.Tax;
        var orderUrl = $"{StoreUrl}/account/orders/{request.OrderNumber}";

        var rows = new StringBuilder();
        foreach (var item in request.Items)
        {
            rows.Append($"""
                <tr>
                  <td style="padding:14px 0;border-bottom:1px solid #ece6dc;">
                    <div style="font-weight:600;color:#1f2a24;">{E(item.Name)}</div>
                    <div style="font-size:12px;color:#7a8079;margin-top:2px;">SKU {E(item.Sku)} &middot; Qty {item.Quantity}</div>
                  </td>
                  <td align="right" style="padding:14px 0;border-bottom:1px solid #ece6dc;font-weight:600;color:#1f2a24;white-space:nowrap;">{Money(item.UnitPrice * item.Quantity)}</td>
                </tr>
                """);
        }

        var body = $"""
            <p style="margin:0 0 20px;">Hi {E(FirstName(request.CustomerName))}, thanks for gearing up with us! We've received your order and are getting it ready at our Portland fulfillment center.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f4ee;border-radius:10px;margin:0 0 24px;">
              <tr>
                <td style="padding:16px 20px;">
                  <div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#7a8079;">Order number</div>
                  <div style="font-size:18px;font-weight:700;color:#1f3d2b;">{E(request.OrderNumber)}</div>
                </td>
                <td align="right" style="padding:16px 20px;">
                  <div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#7a8079;">Order total</div>
                  <div style="font-size:18px;font-weight:700;color:#1f3d2b;">{Money(total)}</div>
                </td>
              </tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              {rows}
              {SummaryRow("Subtotal", Money(subtotal))}
              {SummaryRow("Shipping", request.ShippingCost == 0 ? "Free" : Money(request.ShippingCost))}
              {SummaryRow("Tax", Money(request.Tax))}
              <tr>
                <td style="padding:12px 0 0;font-size:16px;font-weight:700;color:#1f2a24;border-top:2px solid #1f3d2b;">Total</td>
                <td align="right" style="padding:12px 0 0;font-size:16px;font-weight:700;color:#1f2a24;border-top:2px solid #1f3d2b;">{Money(total)}</td>
              </tr>
            </table>
            <div style="margin:28px 0 0;font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#7a8079;">Shipping to</div>
            <div style="margin:6px 0 0;color:#1f2a24;">{E(request.CustomerName)}<br>{E(request.ShipTo.Line1)}<br>{E(request.ShipTo.City)}, {E(request.ShipTo.Region)} {E(request.ShipTo.PostalCode)}</div>
            """;

        var html = Layout(
            preheader: $"Order {request.OrderNumber} is confirmed. We'll email you when it ships.",
            eyebrow: "Order confirmed",
            title: "Your adventure kit is on the way",
            body: body,
            ctaLabel: "View your order",
            ctaUrl: orderUrl);

        var text = new StringBuilder()
            .AppendLine($"Hi {FirstName(request.CustomerName)}, thanks for your order!")
            .AppendLine()
            .AppendLine($"Order {request.OrderNumber}");
        foreach (var item in request.Items)
        {
            text.AppendLine($"  {item.Quantity} x {item.Name} ({item.Sku})  {Money(item.UnitPrice * item.Quantity)}");
        }
        text.AppendLine($"Total: {Money(total)}").AppendLine().AppendLine($"View your order: {orderUrl}");

        return new(EmailTemplate.OrderConfirmation, to, $"Order {request.OrderNumber} confirmed", html, text.ToString(), request.OrderNumber);
    }

    public static RenderedEmail PasswordReset(PasswordResetRequest request, MailboxAddress to, DateTimeOffset expiresAt)
    {
        // The token would normally be persisted by the identity service; here it only lives in the email.
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var resetUrl = $"{StoreUrl}/account/reset-password?token={token}";

        var body = $"""
            <p style="margin:0 0 16px;">Hi {E(FirstName(request.CustomerName))}, we received a request to reset the password for your Contoso Outfitters account.</p>
            <p style="margin:0 0 4px;">Use the button below to choose a new password. For your security, the link works once and expires in <strong>{request.ExpiresInMinutes} minutes</strong>.</p>
            """;

        var footer = $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:28px 0 0;background:#fff7ec;border-left:4px solid #e07a2f;border-radius:6px;">
              <tr><td style="padding:14px 18px;font-size:14px;color:#5b4a36;">
                <strong>Didn't ask for this?</strong> You can safely ignore this email &mdash; your password won't change. If you keep getting these, contact
                <a href="{StoreUrl}/help" style="color:#b85a17;">Contoso Outfitters support</a>.
              </td></tr>
            </table>
            <p style="margin:20px 0 0;font-size:12px;color:#7a8079;">Link expires {expiresAt.ToString("MMMM d 'at' h:mm tt 'UTC'", Usd)}.</p>
            """;

        var html = Layout(
            preheader: $"Reset your password. This link expires in {request.ExpiresInMinutes} minutes.",
            eyebrow: "Account security",
            title: "Reset your password",
            body: body,
            ctaLabel: "Choose a new password",
            ctaUrl: resetUrl,
            afterCta: footer);

        var text = $"""
            Hi {FirstName(request.CustomerName)},

            Reset your Contoso Outfitters password (link expires in {request.ExpiresInMinutes} minutes):
            {resetUrl}

            If you didn't request this, you can ignore this email.
            """;

        return new(EmailTemplate.PasswordReset, to, "Reset your Contoso Outfitters password", html, text);
    }

    public static RenderedEmail ShippingNotification(ShippingNotificationRequest request, MailboxAddress to)
    {
        var (carrierName, trackingUrl) = request.Carrier switch
        {
            Carrier.Ups => ("UPS", $"https://www.ups.com/track?tracknum={Uri.EscapeDataString(request.TrackingNumber)}"),
            Carrier.FedEx => ("FedEx", $"https://www.fedex.com/fedextrack/?trknbr={Uri.EscapeDataString(request.TrackingNumber)}"),
            Carrier.Usps => ("USPS", $"https://tools.usps.com/go/TrackConfirmAction?tLabels={Uri.EscapeDataString(request.TrackingNumber)}"),
            _ => ("DHL", $"https://www.dhl.com/us-en/home/tracking.html?tracking-id={Uri.EscapeDataString(request.TrackingNumber)}")
        };
        var eta = request.EstimatedDelivery.ToString("dddd, MMMM d", Usd);
        var items = request.ItemCount == 1 ? "1 item" : $"{request.ItemCount} items";

        var body = $"""
            <p style="margin:0 0 24px;">Good news, {E(FirstName(request.CustomerName))}! {items} from order <strong>{E(request.OrderNumber)}</strong> left our warehouse and {(request.ItemCount == 1 ? "is" : "are")} headed your way.</p>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
              <tr>
                {Step("Ordered", done: true)}
                {Step("Shipped", done: true)}
                {Step("Out for delivery", done: false)}
                {Step("Delivered", done: false)}
              </tr>
            </table>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f4ee;border-radius:10px;">
              <tr>
                <td style="padding:18px 20px;">
                  <div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#7a8079;">Estimated delivery</div>
                  <div style="font-size:20px;font-weight:700;color:#1f3d2b;margin-top:2px;">{E(eta)}</div>
                </td>
                <td align="right" style="padding:18px 20px;">
                  <div style="font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:#7a8079;">{E(carrierName)} tracking</div>
                  <div style="font-family:Consolas,Menlo,monospace;font-size:15px;color:#1f2a24;margin-top:4px;">{E(request.TrackingNumber)}</div>
                </td>
              </tr>
            </table>
            """;

        var html = Layout(
            preheader: $"Order {request.OrderNumber} shipped with {carrierName}. Arriving {eta}.",
            eyebrow: "Shipped",
            title: "Your order is on its way",
            body: body,
            ctaLabel: $"Track with {carrierName}",
            ctaUrl: trackingUrl);

        var text = $"""
            Good news, {FirstName(request.CustomerName)}! Order {request.OrderNumber} has shipped.

            Carrier: {carrierName}
            Tracking number: {request.TrackingNumber}
            Estimated delivery: {eta}

            Track your package: {trackingUrl}
            """;

        return new(EmailTemplate.ShippingNotification, to, $"Order {request.OrderNumber} has shipped", html, text, request.OrderNumber);
    }

    private static string Layout(string preheader, string eyebrow, string title, string body, string ctaLabel, string ctaUrl, string afterCta = "") => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <meta name="color-scheme" content="light only">
          <title>{E(title)}</title>
        </head>
        <body style="margin:0;padding:0;background:#efe9df;">
          <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{E(preheader)}</div>
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#efe9df;">
            <tr>
              <td align="center" style="padding:32px 16px;">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="width:100%;max-width:600px;font-family:'Segoe UI',Helvetica,Arial,sans-serif;font-size:15px;line-height:1.55;color:#2e3a33;">
                  <tr>
                    <td style="background:#1f3d2b;border-radius:14px 14px 0 0;padding:22px 32px;">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
                        <tr>
                          <td style="font-size:20px;font-weight:700;color:#ffffff;letter-spacing:.02em;">&#9650; Contoso Outfitters</td>
                          <td align="right" style="font-size:12px;color:#b9cdbf;">Gear for every trail</td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                  <tr>
                    <td style="background:#ffffff;padding:36px 32px 32px;">
                      <div style="font-size:12px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:#e07a2f;">{E(eyebrow)}</div>
                      <h1 style="margin:6px 0 20px;font-size:26px;line-height:1.25;color:#1f2a24;">{E(title)}</h1>
                      {body}
                      <table role="presentation" cellpadding="0" cellspacing="0" style="margin:28px 0 0;">
                        <tr><td style="background:#e07a2f;border-radius:8px;">
                          <a href="{E(ctaUrl)}" style="display:inline-block;padding:13px 26px;color:#ffffff;font-weight:700;text-decoration:none;">{E(ctaLabel)} &rarr;</a>
                        </td></tr>
                      </table>
                      {afterCta}
                    </td>
                  </tr>
                  <tr>
                    <td style="background:#f7f4ee;border-radius:0 0 14px 14px;padding:22px 32px;font-size:12px;color:#7a8079;">
                      Contoso Outfitters &middot; 1200 NW Naito Pkwy, Portland, OR 97209<br>
                      Questions? Reply to this email or visit <a href="{StoreUrl}/help" style="color:#1f3d2b;">contoso-outfitters.example/help</a>.
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;

    private static string SummaryRow(string label, string value) => $"""
        <tr>
          <td style="padding:8px 0 0;color:#5f665f;">{label}</td>
          <td align="right" style="padding:8px 0 0;color:#5f665f;">{value}</td>
        </tr>
        """;

    private static string Step(string label, bool done) => $"""
        <td align="center" width="25%" style="padding:0 4px;">
          <div style="height:6px;border-radius:3px;background:{(done ? "#2f7a4f" : "#e2ddd3")};"></div>
          <div style="margin-top:8px;font-size:12px;font-weight:{(done ? "700" : "400")};color:{(done ? "#1f3d2b" : "#9aa09a")};">{(done ? "&#10003; " : "")}{label}</div>
        </td>
        """;

    private static string FirstName(string name) => name.Trim().Split(' ', 2)[0];

    private static string Money(decimal amount) => amount.ToString("C", Usd);

    private static string E(string value) => HtmlEncoder.Default.Encode(value);
}
