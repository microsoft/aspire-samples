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
    var message = CreateMessage(
        request.Email,
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
    var message = CreateMessage(
        request.Email,
        "Aspire newsletter subscription ended",
        "You have been unsubscribed from the Aspire newsletter.");
    var client = await mailKit.GetSmtpClientAsync(cancellationToken);
    await client.SendAsync(message, cancellationToken);

    return Results.Ok(new { request.Email, Status = "unsubscribed" });
})
.WithName("Unsubscribe");

app.Run();

static MimeMessage CreateMessage(string email, string subject, string body)
{
    var message = new MimeMessage();
    message.From.Add(new MailboxAddress("Aspire Newsletter", "newsletter@example.com"));
    message.To.Add(MailboxAddress.Parse(email));
    message.Subject = subject;
    message.Body = new TextPart("plain") { Text = body };
    return message;
}

internal sealed record SubscriptionRequest(string Email);