using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var maildev = builder.AddMailDev("maildev");

builder.AddDotnetProject("notifications", "../NotificationService/NotificationService.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(maildev)
    .WaitFor(maildev)
    .WithUrlForEndpoint("http", url => url.DisplayText = "Notifications API")
    .WithUrls(context =>
    {
        var endpoint = context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    })
    .WithHttpCommand("/dev/sample-emails", "Send sample emails", commandName: "send-sample-emails", commandOptions: new()
    {
        Description = "POST /dev/sample-emails: send order confirmations, shipping notifications, and password resets for sample customers.",
        IconName = "MailMultiple",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });

builder.Build().Run();
