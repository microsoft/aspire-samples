import { createBuilder } from "./.aspire/modules/aspire.mjs";

const builder = await createBuilder();

await builder.addPythonApp("script", "./script", "main.py");

await builder.build().run();
