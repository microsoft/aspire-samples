import {
    createBuilder,
    HttpCommandResultMode,
    OtlpProtocol,
    refExpr,
} from './.aspire/modules/aspire.mjs';

const builder = await createBuilder();
const executionContext = await builder.executionContext();
const isRunMode = await executionContext.isRunMode();

// No persistent volume: use disposable data. The authenticated Redis REPL is for trusted dashboard users only.
const redis = await builder.addRedis('redis').withRepl();

// Local demo: anyone who can reach this unauthenticated HTTP API can read, replace, or delete demo notes.
// tsx runs the TypeScript directly, and the OpenTelemetry SDK is preloaded so it can patch Express,
// node-redis and pino before the app imports them. Telemetry goes to the dashboard over OTLP/HTTP.
const api = await builder.addNodeApp('api', './api', 'src/index.ts')
    .withEnvironment('NODE_OPTIONS', '--import tsx --import ./src/instrumentation.ts')
    .withHttpEndpoint({ env: 'PORT' })
    .withHttpHealthCheck({ path: '/health' })
    .withReference(redis)
    .withOtlpExporter({ protocol: OtlpProtocol.HttpProtobuf })
    .waitFor(redis)
    .withUrlForEndpoint('http', async (url) => {
        url.displayText = 'Notes API';
    });

const apiEndpoint = await api.getEndpoint('http');
await api
    .withUrl(refExpr`${apiEndpoint}/scalar`, { displayText: 'API reference (Scalar)' })
    .withUrl(refExpr`${apiEndpoint}/openapi.json`, { displayText: 'OpenAPI JSON' });

if (isRunMode) {
    // A run-mode-only endpoint plus a dashboard button that writes realistic notes for Slumber's List notes request.
    await api
        .withEnvironment('ENABLE_DEMO_ENDPOINTS', 'true')
        .withHttpCommand('/dev/notes/seed', 'Seed sample notes', {
            commandName: 'seed-notes',
            description: 'POST /dev/notes/seed: writes five team notes for Slumber\'s List notes request. The welcome note is left alone.',
            methodName: 'POST',
            iconName: 'NoteAdd',
            isHighlighted: true,
            resultMode: HttpCommandResultMode.Json,
        });
}

await builder.addDockerfile('slumber', './slumber')
    .withEnvironment('BASE_URL', api.getEndpoint('http'))
    .waitFor(api)
    .withTerminal();

await builder.build().run();
