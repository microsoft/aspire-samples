using MailKit.Client;
using MimeKit;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMailKitClient("maildev");
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapScalarApiReference();
app.MapDefaultEndpoints();

app.MapPost("/subscribe", async (
    SubscriptionRequest request,
    MailKitClientFactory mailKit,
    CancellationToken cancellationToken) =>
{
    if (!TryCreateRecipient(request.Email, out var recipient))
    {
        return Results.BadRequest(new { request.Email, Error = "A valid email address is required." });
    }

    var message = CreateMessage(
        recipient,
        "Welcome to the Aspire newsletter",
        "You are now subscribed to the Aspire newsletter.");
    var client = await mailKit.GetSmtpClientAsync(cancellationToken);
    await client.SendAsync(message, cancellationToken);

    return Results.Accepted(value: new { request.Email, Status = "subscribed" });
})
.WithName("Subscribe");

app.MapPost("/unsubscribe", async (
    SubscriptionRequest request,
    MailKitClientFactory mailKit,
    CancellationToken cancellationToken) =>
{
    if (!TryCreateRecipient(request.Email, out var recipient))
    {
        return Results.BadRequest(new { request.Email, Error = "A valid email address is required." });
    }

    var message = CreateMessage(
        recipient,
        "Aspire newsletter subscription ended",
        "You have been unsubscribed from the Aspire newsletter.");
    var client = await mailKit.GetSmtpClientAsync(cancellationToken);
    await client.SendAsync(message, cancellationToken);

    return Results.Ok(new { request.Email, Status = "unsubscribed" });
})
.WithName("Unsubscribe");

app.Run();

static bool TryCreateRecipient(string? email, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MailboxAddress? recipient)
{
    if (string.IsNullOrWhiteSpace(email))
    {
        recipient = null;
        return false;
    }

    return MailboxAddress.TryParse(email, out recipient);
}

static MimeMessage CreateMessage(MailboxAddress recipient, string subject, string body)
{
    var message = new MimeMessage();
    message.From.Add(new MailboxAddress("Aspire Newsletter", "newsletter@example.com"));
    message.To.Add(recipient);
    message.Subject = subject;
    message.Body = new TextPart("plain") { Text = body };
    return message;
}

internal sealed record SubscriptionRequest(string Email);