import { createBuilder, HttpCommandResultMode, refExpr } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

// MailDev catches every email in development. addMailDev is generated from the C# hosting
// integration in MailDev.Hosting through the Aspire Type System.
const maildev = await builder.addMailDev("maildev");

// The notification service sends Contoso Outfitters' transactional email through the
// MailKit client integration, which reads the "maildev" connection string.
const notifications = await builder.addDotnetProject("notifications", "./NotificationService/NotificationService.csproj")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .withReference(maildev)
    .waitFor(maildev)
    .withUrlForEndpoint("http", async (url) => {
        url.displayText = "Notifications API";
    });

const endpoint = await notifications.getEndpoint("http");
await notifications
    .withUrl(refExpr`${endpoint}/scalar`, { displayText: "API reference (Scalar)" })
    .withUrl(refExpr`${endpoint}/openapi/v1.json`, { displayText: "OpenAPI JSON" })
    // Fills the MailDev inbox and lights up traces, metrics, and logs in one click.
    .withHttpCommand("/dev/sample-emails", "Send sample emails", {
        commandName: "send-sample-emails",
        description: "POST /dev/sample-emails: send order confirmations, shipping notifications, and password resets for sample customers.",
        methodName: "POST",
        iconName: "MailMultiple",
        isHighlighted: true,
        resultMode: HttpCommandResultMode.Json,
    });

await builder.build().run();
