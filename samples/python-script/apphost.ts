import { createBuilder } from "./.modules/aspire.js";

const builder = await createBuilder();

await builder.addPythonApp("script", "./script", "main.py");

await builder.build().run();
