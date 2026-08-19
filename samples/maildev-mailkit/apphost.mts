import { createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

const maildev = await builder.addMailDev("maildev");

await builder.addCSharpApp("newsletterservice", "./NewsletterService")
    .withHttpHealthCheck({ path: "/health" })
    .withExternalHttpEndpoints()
    .withReference(maildev)
    .waitFor(maildev);

await builder.build().run();