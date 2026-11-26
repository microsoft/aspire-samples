using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NotificationService;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMailKitClient("maildev");

builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificationTelemetry>();
builder.Services.AddScoped<EmailSender>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "Contoso Outfitters Notifications API",
            Version = "v1",
            Description = """
                Sends Contoso Outfitters' transactional email (order confirmations, password resets, and shipping
                notifications) through the MailKit client integration. In development, messages land in the
                MailDev inbox linked from the Aspire dashboard.
                """
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Emails", Description = "Render a branded HTML + plain-text email and hand it to the SMTP server." },
            new() { Name = "Templates", Description = "Browse and preview the email templates without sending anything." }
        };
        return Task.CompletedTask;
    });

    // Show realistic request bodies in the API reference.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        object? example = context.JsonTypeInfo.Type switch
        {
            var t when t == typeof(OrderConfirmationRequest) => SampleEmails.Order,
            var t when t == typeof(PasswordResetRequest) => SampleEmails.PasswordReset,
            var t when t == typeof(ShippingNotificationRequest) => SampleEmails.Shipment,
            _ => null
        };
        if (example is not null)
        {
            var json = context.ApplicationServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            schema.Example = JsonSerializer.SerializeToNode(example, example.GetType(), json);
        }
        return Task.CompletedTask;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Contoso Outfitters Notifications API")
    .SortTagsAlphabetically());
app.MapDefaultEndpoints();

app.MapEmailApi();

if (app.Environment.IsDevelopment())
{
    app.MapSampleEmailsEndpoint();
}

app.Run();
