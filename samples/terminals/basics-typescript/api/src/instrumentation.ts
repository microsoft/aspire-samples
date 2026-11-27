/**
 * OpenTelemetry bootstrap for the notes API.
 *
 * Preload this module before the app (`--import ./src/instrumentation.ts`; the AppHost sets
 * NODE_OPTIONS) so the instrumentations can patch http, Express, node-redis and pino before
 * `index.ts` imports them.
 *
 * Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT / _HEADERS / _PROTOCOL and OTEL_SERVICE_NAME.
 * The AppHost asks for OTLP over HTTP/protobuf: the gRPC exporter (@grpc/grpc-js) ignores
 * NODE_EXTRA_CA_CERTS, so it can't verify the dashboard's development certificate.
 */
import { register } from 'node:module';
import { env } from 'node:process';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-proto';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { ExpressInstrumentation, ExpressLayerType } from '@opentelemetry/instrumentation-express';
import { HttpInstrumentation } from '@opentelemetry/instrumentation-http';
import { PinoInstrumentation } from '@opentelemetry/instrumentation-pino';
import { RedisInstrumentation } from '@opentelemetry/instrumentation-redis';
import { RuntimeNodeInstrumentation } from '@opentelemetry/instrumentation-runtime-node';
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { NodeSDK } from '@opentelemetry/sdk-node';

let sdk: NodeSDK | undefined;
if (env.OTEL_EXPORTER_OTLP_ENDPOINT) {
    // The app imports Express, pino and node-redis as ES modules, which bypasses require()
    // patching. The import-in-the-middle loader hook lets the instrumentations patch them.
    register('@opentelemetry/instrumentation/hook.mjs', import.meta.url);

    sdk = new NodeSDK({
        traceExporter: new OTLPTraceExporter(),
        metricReaders: [
            new PeriodicExportingMetricReader({
                exporter: new OTLPMetricExporter(),
                // Aspire sets a short interval in run mode so charts update quickly.
                exportIntervalMillis: Number(env.OTEL_METRIC_EXPORT_INTERVAL ?? 60_000),
            }),
        ],
        logRecordProcessors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter() })],
        instrumentations: [
            // Server spans for incoming requests; the dashboard's health probe is skipped so it doesn't flood the trace list.
            new HttpInstrumentation({ ignoreIncomingRequestHook: request => request.url === '/health' }),
            // Names server spans after the matched route (middleware such as the JSON body parser is skipped).
            new ExpressInstrumentation({ ignoreLayersType: [ExpressLayerType.MIDDLEWARE] }),
            // A client span per Redis command (HGET, HSET, ...); health-check PINGs have no parent and are skipped.
            new RedisInstrumentation({ requireParentSpan: true }),
            // Bridges pino records to OTLP logs and stamps them with trace_id/span_id.
            new PinoInstrumentation(),
            // Event loop, GC and heap metrics.
            new RuntimeNodeInstrumentation(),
        ],
    });
    sdk.start();
}

export async function shutdownTelemetry() {
    await sdk?.shutdown();
}
