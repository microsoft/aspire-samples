import { createBuilder } from './.modules/aspire.js';

const builder = await createBuilder();

const weatherApi = await builder.addProject("weatherapi", "../AspireJavaScript.MinimalApi/AspireJavaScript.MinimalApi.csproj", "https")
    .withExternalHttpEndpoints();

await builder.addJavaScriptApp("angular", "../AspireJavaScript.Angular", { runScriptName: "start" })
    .withReference(weatherApi)
    .waitFor(weatherApi)
    .withHttpEndpoint({ env: "PORT" })
    .withExternalHttpEndpoints()
    .publishAsDockerFile();

await builder.addJavaScriptApp("react", "../AspireJavaScript.React", { runScriptName: "start" })
    .withReference(weatherApi)
    .waitFor(weatherApi)
    .withEnvironment("BROWSER", "none")
    .withHttpEndpoint({ env: "PORT" })
    .withExternalHttpEndpoints()
    .publishAsDockerFile();

await builder.addJavaScriptApp("vue", "../AspireJavaScript.Vue", { runScriptName: "start" })
    .withReference(weatherApi)
    .waitFor(weatherApi)
    .withHttpEndpoint({ env: "PORT" })
    .withExternalHttpEndpoints()
    .publishAsDockerFile();

const reactVite = await builder.addViteApp("reactvite", "../AspireJavaScript.Vite")
    .withReference(weatherApi)
    .withEnvironment("BROWSER", "none");

await weatherApi.publishWithContainerFiles(reactVite, "./wwwroot");

await builder.build().run();
