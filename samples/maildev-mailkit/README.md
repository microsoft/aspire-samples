# MailDev and MailKit custom integrations

This sample shows how to build custom Aspire hosting and client integrations with Aspire 17.0, using a realistic scenario: **Contoso Outfitters**, an outdoor gear retailer, sends transactional email. A notification service renders branded HTML order confirmations, password resets, and shipping notifications and sends them through a custom **MailKit** client integration. In development, a custom **MailDev** hosting integration catches every message, so you can read the emails in a local inbox. The runnable AppHost is written in TypeScript and consumes the C# hosting integration through the Aspire Type System (ATS).

![MailDev inbox filled with Contoso Outfitters order confirmations, shipping notifications and password resets, with a branded order confirmation email open (light theme)](./images/maildev-mailkit-inbox-light.png#gh-light-mode-only)
![MailDev inbox filled with Contoso Outfitters order confirmations, shipping notifications and password resets, with a branded order confirmation email open (dark theme)](./images/maildev-mailkit-inbox-dark.png#gh-dark-mode-only)

## Projects

- `MailDev.Hosting` models a MailDev container, its web and SMTP endpoints, a secret SMTP password, and a deferred connection string. The dashboard shows the web endpoint as **MailDev inbox**.
- `MailKit.Client` registers a scoped MailKit SMTP client, health checks, and MailKit's built-in tracing and metrics in a consuming service.
- `NotificationService` is the Contoso Outfitters notifications API. It validates requests, renders HTML and plain-text emails, and sends them through the MailKit client. It also records domain traces, metrics, and structured logs.
- `ServiceDefaults` configures standard Aspire health checks and OpenTelemetry, including every `Contoso.*` meter and activity source.
- `CSharpAppHost` is a compile-validated C# equivalent of the runnable TypeScript AppHost.

## Aspire Type System

The hosting integration marks `MailDevResource` and `AddMailDev` with `[AspireExport]`. The local project reference in `aspire.config.json` lets `aspire restore` inspect those exports and generate the TypeScript `addMailDev` API under `.aspire/modules`.

Generated files under `.aspire/modules` are not source files and must not be edited or committed.

```json
"packages": {
  "MailDev.Hosting": "MailDev.Hosting/MailDev.Hosting.csproj"
}
```

The TypeScript AppHost uses the generated API. It also adds dashboard links to the API reference and a **Send sample emails** command:

```typescript
const maildev = await builder.addMailDev("maildev");

const notifications = await builder.addDotnetProject("notifications", "./NotificationService/NotificationService.csproj")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .withReference(maildev)
    .waitFor(maildev);

const endpoint = await notifications.getEndpoint("http");
await notifications
    .withUrl(refExpr`${endpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${endpoint}/openapi/v1.json`, { displayText: "OpenAPI JSON" })
    .withHttpCommand("/dev/sample-emails", "Send sample emails", {
        commandName: "send-sample-emails",
        methodName: "POST",
        iconName: "MailMultiple",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    });
```

The equivalent C# AppHost code is:

```csharp
var maildev = builder.AddMailDev("maildev");

builder.AddDotnetProject("notifications", "../NotificationService/NotificationService.csproj")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(maildev)
    .WaitFor(maildev)
    .WithUrls(context =>
    {
        var endpoint = context.GetEndpoint("http");
        context.Urls.Add(new() { Url = "/scalar", DisplayText = "API reference (Scalar)", Endpoint = endpoint });
        context.Urls.Add(new() { Url = "/openapi/v1.json", DisplayText = "OpenAPI JSON", Endpoint = endpoint });
    })
    .WithHttpCommand("/dev/sample-emails", "Send sample emails", commandName: "send-sample-emails", commandOptions: new()
    {
        IconName = "MailMultiple",
        IsHighlighted = true,
        ResultMode = HttpCommandResultMode.Json
    });
```

## Secure credentials

`AddMailDev` creates a secret password parameter by default. The password is passed to MailDev through `MAILDEV_INCOMING_PASS` and to the notification service through a deferred connection string:

```text
Endpoint=smtp://{maildev.bindings.smtp.host}:{maildev.bindings.smtp.port};Username=mail-dev;Password={maildev-password.value}
```

The AppHost model retains parameter and endpoint references instead of embedding a password or allocated port in source code. MailKit parses the resolved connection string, connects to SMTP, and authenticates once per service scope.

## Run the sample

Prerequisites are .NET 10, Node.js 24 or a supported Node.js 20/22 release, Docker, and the [Aspire 17.0 CLI targeted by this repository](../../README.md#aspire-version).

```powershell
aspire restore
npm install
npm run aspire:build
aspire run
```

In the Aspire dashboard, run **Send sample emails** on the **notifications** resource, then open **MailDev inbox** on the **maildev** resource to read the emails.

## Notifications API

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/emails/order-confirmations` | Itemized receipt with totals and the ship-to address |
| `POST` | `/api/emails/password-resets` | Single-use reset link that expires after `expiresInMinutes` (5–120) |
| `POST` | `/api/emails/shipping-notifications` | Carrier, tracking link, delivery progress, and estimated delivery date |
| `GET` | `/api/emails/templates` | Lists the templates and the endpoint that sends each one |
| `GET` | `/api/emails/templates/{template}/preview` | Renders a template with sample data, without sending it |
| `POST` | `/dev/sample-emails` | Development only: sends six emails across every template (used by the dashboard command) |

Successful sends return `202 Accepted` with a receipt that holds the `Message-ID` and the masked recipient (for example `m***@example.com`). Invalid requests return `400` validation `ProblemDetails`, an unknown template preview returns `404`, and an unreachable SMTP server returns `503`. `NotificationService/NotificationService.http` has a request for each case.

```http
POST /api/emails/shipping-notifications
Content-Type: application/json

{
  "email": "maya.chen@example.com",
  "customerName": "Maya Chen",
  "orderNumber": "CO-104582",
  "carrier": "ups",
  "trackingNumber": "1Z999AA10123456784",
  "estimatedDelivery": "2026-11-27",
  "itemCount": 4
}
```

![Scalar API reference for the Contoso Outfitters Notifications API showing the Send an order confirmation operation with its request schema and example (light theme)](./images/maildev-mailkit-scalar-light.png#gh-light-mode-only)
![Scalar API reference for the Contoso Outfitters Notifications API showing the Send an order confirmation operation with its request schema and example (dark theme)](./images/maildev-mailkit-scalar-dark.png#gh-dark-mode-only)

## What to explore in the Aspire dashboard

### Resources and commands

![Aspire dashboard resources page with maildev and notifications running, MailDev inbox and API reference links, and the Send sample emails command highlighted (light theme)](./images/maildev-mailkit-dashboard-resources-light.png#gh-light-mode-only)
![Aspire dashboard resources page with maildev and notifications running, MailDev inbox and API reference links, and the Send sample emails command highlighted (dark theme)](./images/maildev-mailkit-dashboard-resources-dark.png#gh-dark-mode-only)

- **maildev** links to the **MailDev inbox**. Its SMTP endpoint is only listed on the resource details page.
- **notifications** links to **API reference (Scalar)** and **OpenAPI JSON**, and has a **Send sample emails** command. The command sends two order confirmations, two shipping notifications, and two password resets for sample customers, and returns a JSON summary such as `{"sent":6,"failed":0,"receipts":[...]}`.
- Stop **maildev** and send an email to see the failure path: the API returns `503` and the failure shows up in traces, metrics, and logs.

### Traces

![Aspire dashboard trace of the Send sample emails command, with six domain send spans wrapping MailKit Connect, Authenticate and Send client spans, and the span details showing the template, masked recipient and order number (light theme)](./images/maildev-mailkit-trace-light.png#gh-light-mode-only)
![Aspire dashboard trace of the Send sample emails command, with six domain send spans wrapping MailKit Connect, Authenticate and Send client spans, and the span details showing the template, masked recipient and order number (dark theme)](./images/maildev-mailkit-trace-dark.png#gh-dark-mode-only)

- Each email is a `send <template> email` span from the `Contoso.Notifications` activity source. It carries `email.template`, `email.recipient` (masked), `email.subject`, `email.message_id`, `email.size`, and `contoso.order.number`, plus `email.rendered` and `smtp.accepted` events (with the SMTP server response).
- MailKit's own `Connect`, `Authenticate`, and `Send` client spans (from `MailKit.Net.SmtpClient`) nest under the domain span. A batch reuses one SMTP connection, so only the first email connects and authenticates.
- Failed sends set the span status to error, record the exception, and add `error.type`.

### Metrics

![Aspire dashboard metrics page charting contoso.notifications.emails.sent, the number of emails accepted by the SMTP server per interval (light theme)](./images/maildev-mailkit-metrics-light.png#gh-light-mode-only)
![Aspire dashboard metrics page charting contoso.notifications.emails.sent, the number of emails accepted by the SMTP server per interval (dark theme)](./images/maildev-mailkit-metrics-dark.png#gh-dark-mode-only)

| Meter | Instrument | Description |
| --- | --- | --- |
| `Contoso.Notifications` | `contoso.notifications.emails.sent` | Emails accepted by the SMTP server, by `email.template` |
| `Contoso.Notifications` | `contoso.notifications.emails.failed` | Failed sends, by `email.template` and `error.type` |
| `Contoso.Notifications` | `contoso.notifications.email.send.duration` | Histogram of SMTP hand-off time, in seconds, by template and `outcome` |
| `Contoso.Notifications` | `contoso.notifications.email.size` | Histogram of rendered MIME message size, in bytes |
| `Contoso.Notifications` | `contoso.notifications.emails.in_flight` | Up-down counter of sends in progress |
| `mailkit.net.smtp` | `mailkit.net.*` | MailKit's built-in SMTP client metrics |

Metric tags are limited to the template, outcome, and error type. Recipients and order numbers never become metric tags.

### Structured logs

Log messages use source-generated `[LoggerMessage]` methods in `NotificationService/Log.cs`, so `Template`, `MessageId`, `Recipient`, `ElapsedMilliseconds`, and `ErrorType` are filterable fields. Recipients are always masked (for example `d***@example.net`), and password reset tokens are never logged. Each entry links to the trace that produced it.

## Tests

```powershell
dotnet test MailDev.Hosting.Tests/MailDev.Hosting.Tests.csproj
dotnet test MailKit.Client.Tests/MailKit.Client.Tests.csproj
npm run aspire:build
npm run aspire:lint
dotnet build CSharpAppHost/CSharpAppHost.csproj
```
