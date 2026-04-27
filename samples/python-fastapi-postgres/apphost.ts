import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();

await builder.addDockerComposeEnvironment("dc");

const postgres = await builder.addPostgres("postgres")
    .withPgAdmin();
const db = await postgres.addDatabase("db");

await builder.addUvicornApp("api", "./api", "main:app")
    .withExternalHttpEndpoints()
    .waitFor(db)
    .withReference(db)
    .withReference(postgres);

await builder.build().run();
