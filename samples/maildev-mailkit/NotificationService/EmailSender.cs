using System.Diagnostics;
using MailKit.Client;
using MimeKit;

namespace NotificationService;

/// <summary>A rendered email ready to send.</summary>
public sealed record RenderedEmail(
    EmailTemplate Template,
    MailboxAddress To,
    string Subject,
    string Html,
    string Text,
    string? OrderNumber = null);

/// <summary>Thrown when the SMTP server can't be reached or rejects a message.</summary>
public sealed class EmailDeliveryException(string errorType, Exception innerException)
    : Exception("The mail server did not accept the message.", innerException)
{
    public string ErrorType { get; } = errorType;
}

/// <summary>Hands rendered emails to the SMTP server through the MailKit client integration.</summary>
public sealed class EmailSender(
    MailKitClientFactory mailKit,
    NotificationTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<EmailSender> logger)
{
    private static readonly MailboxAddress From = new("Contoso Outfitters", "orders@contoso-outfitters.example");

    public async Task<EmailReceipt> SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        var template = email.Template.ToSlug();
        var recipient = EmailMasking.Mask(email.To.Address);

        // MailKit's own "MailKit.Net.SmtpClient" spans (connect, authenticate, send) nest under this one.
        using var activity = NotificationTelemetry.ActivitySource.StartActivity($"send {template} email");
        activity?.SetTag("email.template", template);
        activity?.SetTag("email.recipient", recipient);
        activity?.SetTag("email.subject", email.Subject);
        activity?.SetTag("contoso.order.number", email.OrderNumber);

        var message = new MimeMessage();
        message.From.Add(From);
        message.To.Add(email.To);
        message.Subject = email.Subject;
        message.Headers.Add("X-Contoso-Template", template);
        message.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text }.ToMessageBody();
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("contoso-outfitters.example");

        var size = MeasureSize(message);
        activity?.SetTag("email.message_id", message.MessageId);
        activity?.SetTag("email.size", size);
        activity?.AddEvent(new ActivityEvent("email.rendered", tags: new()
        {
            ["email.size"] = size,
            ["email.parts"] = message.BodyParts.Count()
        }));

        telemetry.SendStarted();
        var start = timeProvider.GetTimestamp();
        try
        {
            var client = await mailKit.GetSmtpClientAsync(cancellationToken);
            var response = await client.SendAsync(message, cancellationToken);
            var elapsed = timeProvider.GetElapsedTime(start);

            telemetry.Sent(email.Template, elapsed, size);
            activity?.AddEvent(new ActivityEvent("smtp.accepted", tags: new() { ["smtp.response"] = response }));
            logger.EmailSent(template, message.MessageId, recipient, elapsed.TotalMilliseconds);

            return new EmailReceipt(message.MessageId, email.Template, recipient, email.Subject, timeProvider.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var errorType = exception.GetType().Name;
            telemetry.Failed(email.Template, timeProvider.GetElapsedTime(start), errorType);
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity?.SetTag("error.type", errorType);
            logger.EmailSendFailed(exception, template, recipient, errorType);
            throw new EmailDeliveryException(errorType, exception);
        }
    }

    private static long MeasureSize(MimeMessage message)
    {
        using var stream = new MimeKit.IO.MeasuringStream();
        message.WriteTo(stream);
        return stream.Length;
    }
}

public static class EmailMasking
{
    /// <summary>Masks the local part of an address so logs and spans never hold the full email.</summary>
    /// <example><c>maya.chen@example.com</c> becomes <c>m***@example.com</c>.</example>
    public static string Mask(string address)
    {
        var at = address.LastIndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }
}

public static class EmailTemplateExtensions
{
    public static string ToSlug(this EmailTemplate template) => template switch
    {
        EmailTemplate.OrderConfirmation => "order-confirmation",
        EmailTemplate.PasswordReset => "password-reset",
        EmailTemplate.ShippingNotification => "shipping-notification",
        _ => throw new ArgumentOutOfRangeException(nameof(template))
    };
}
