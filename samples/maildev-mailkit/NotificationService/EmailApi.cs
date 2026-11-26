using System.ComponentModel;
using Microsoft.AspNetCore.Http.HttpResults;
using MimeKit;

namespace NotificationService;

public static class EmailApi
{
    public static RouteGroupBuilder MapEmailApi(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/emails")
            .WithTags("Emails")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("order-confirmations", (OrderConfirmationRequest request, EmailSender sender, CancellationToken cancellationToken) =>
                SendAsync(request.Email, request.CustomerName, to => EmailTemplates.OrderConfirmation(request, to), sender, cancellationToken))
            .WithName("SendOrderConfirmation")
            .WithSummary("Send an order confirmation")
            .WithDescription("""
                Renders an itemized receipt (line items, subtotal, shipping, tax, total, and ship-to address)
                and hands it to the SMTP server. Call it once checkout has captured payment.
                """);

        group.MapPost("password-resets", (PasswordResetRequest request, EmailSender sender, TimeProvider timeProvider, ILogger<EmailSender> logger, CancellationToken cancellationToken) =>
            {
                var expiresAt = timeProvider.GetUtcNow().AddMinutes(request.ExpiresInMinutes);
                logger.PasswordResetIssued(EmailMasking.Mask(request.Email), expiresAt);
                return SendAsync(request.Email, request.CustomerName, to => EmailTemplates.PasswordReset(request, to, expiresAt), sender, cancellationToken);
            })
            .WithName("SendPasswordReset")
            .WithSummary("Send a password reset link")
            .WithDescription("""
                Generates a single-use reset token and emails a link that expires after `expiresInMinutes`.
                The token is never returned or logged; only the masked recipient appears in telemetry.
                """);

        group.MapPost("shipping-notifications", (ShippingNotificationRequest request, EmailSender sender, CancellationToken cancellationToken) =>
                SendAsync(request.Email, request.CustomerName, to => EmailTemplates.ShippingNotification(request, to), sender, cancellationToken))
            .WithName("SendShippingNotification")
            .WithSummary("Send a shipping notification")
            .WithDescription("Tells the customer their parcel shipped, with a delivery progress bar, the estimated delivery date, and a carrier tracking link.");

        var templates = routes.MapGroup("/api/emails/templates").WithTags("Templates");

        templates.MapGet("", () => TypedResults.Ok(EmailTemplates.All))
            .WithName("ListTemplates")
            .WithSummary("List email templates")
            .WithDescription("Returns every transactional email template and the endpoint that sends it.");

        templates.MapGet("{template}/preview", Results<ContentHttpResult, ProblemHttpResult> (
                [Description("The template identifier, such as `order-confirmation`.")] string template,
                TimeProvider timeProvider) =>
            {
                var to = new MailboxAddress("Preview", "preview@example.com");
                var rendered = template switch
                {
                    "order-confirmation" => EmailTemplates.OrderConfirmation(SampleEmails.Order, to),
                    "password-reset" => EmailTemplates.PasswordReset(SampleEmails.PasswordReset, to, timeProvider.GetUtcNow().AddMinutes(30)),
                    "shipping-notification" => EmailTemplates.ShippingNotification(SampleEmails.Shipment, to),
                    _ => null
                };

                return rendered is null
                    ? TypedResults.Problem(
                        title: "Email template not found",
                        detail: $"No template named '{template}'. Use one of: {string.Join(", ", EmailTemplates.All.Select(t => t.Template.ToSlug()))}.",
                        statusCode: StatusCodes.Status404NotFound)
                    : TypedResults.Text(rendered.Html, "text/html");
            })
            .WithName("PreviewTemplate")
            .WithSummary("Preview a template")
            .WithDescription("Renders a template with sample data and returns the HTML without sending anything.")
            .Produces(StatusCodes.Status200OK, contentType: "text/html")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>Sends a realistic batch of every template so traces, metrics, logs, and the MailDev inbox populate.</summary>
    public static void MapSampleEmailsEndpoint(this IEndpointRouteBuilder routes) =>
        routes.MapPost("/dev/sample-emails", async (EmailSender sender, TimeProvider timeProvider, ILogger<EmailSender> logger, CancellationToken cancellationToken) =>
            {
                var receipts = new List<EmailReceipt>();
                var failed = 0;
                var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

                foreach (var request in SampleEmails.Batch(today))
                {
                    var email = request switch
                    {
                        OrderConfirmationRequest order => EmailTemplates.OrderConfirmation(order, new(order.CustomerName, order.Email)),
                        PasswordResetRequest reset => EmailTemplates.PasswordReset(reset, new(reset.CustomerName, reset.Email), timeProvider.GetUtcNow().AddMinutes(reset.ExpiresInMinutes)),
                        ShippingNotificationRequest shipment => EmailTemplates.ShippingNotification(shipment, new(shipment.CustomerName, shipment.Email)),
                        _ => throw new InvalidOperationException($"Unexpected sample {request.GetType().Name}.")
                    };

                    try
                    {
                        receipts.Add(await sender.SendAsync(email, cancellationToken));
                    }
                    catch (EmailDeliveryException)
                    {
                        failed++;
                    }
                }

                logger.SampleBatchSent(receipts.Count, failed);
                return TypedResults.Ok(new SampleBatchResult(receipts.Count, failed, receipts));
            })
            .ExcludeFromDescription();

    private static async Task<Results<Accepted<EmailReceipt>, ValidationProblem, ProblemHttpResult>> SendAsync(
        string email,
        string customerName,
        Func<MailboxAddress, RenderedEmail> render,
        EmailSender sender,
        CancellationToken cancellationToken)
    {
        // [EmailAddress] is intentionally loose; MimeKit's parser is the authority on what SMTP accepts.
        if (!MailboxAddress.TryParse(email, out var parsed) || parsed.Address != email.Trim())
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["Email"] = ["Enter a single, valid email address."] },
                title: "Invalid email address");
        }

        try
        {
            var receipt = await sender.SendAsync(render(new MailboxAddress(customerName, parsed.Address)), cancellationToken);
            return TypedResults.Accepted((string?)null, receipt);
        }
        catch (EmailDeliveryException exception)
        {
            return TypedResults.Problem(
                title: "Mail server unavailable",
                detail: "The SMTP server did not accept the message. Retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["errorType"] = exception.ErrorType });
        }
    }
}
