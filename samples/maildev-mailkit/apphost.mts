import { createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

const maildev = await builder.addMailDev("maildev");

await builder.addDotnetProject("newsletterservice", "./NewsletterService/NewsletterService.csproj")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .withReference(maildev)
    .waitFor(maildev);

await builder.build().run();