/**
 * OpenTelemetry bootstrap for the Node.js worker, preloaded with `node --import ./instrumentation.js`
 * (the AppHost sets NODE_OPTIONS). Same setup as the API: OTLP over HTTP/protobuf for traces,
 * metrics and logs, the pino log bridge and runtime metrics. Message spans are created in
 * messaging.js; there are no HTTP endpoints to instrument.
 */
import { register } from 'node:module';
import { env } from 'node:process';
import { OTLPLogExporter } from '@opentelemetry/exporter-logs-otlp-proto';
import { OTLPMetricExporter } from '@opentelemetry/exporter-metrics-otlp-proto';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { PinoInstrumentation } from '@opentelemetry/instrumentation-pino';
import { RuntimeNodeInstrumentation } from '@opentelemetry/instrumentation-runtime-node';
import { BatchLogRecordProcessor } from '@opentelemetry/sdk-logs';
import { PeriodicExportingMetricReader } from '@opentelemetry/sdk-metrics';
import { NodeSDK } from '@opentelemetry/sdk-node';

if (env.OTEL_EXPORTER_OTLP_ENDPOINT) {
  // Lets the pino instrumentation patch pino when it's imported as an ES module.
  register('@opentelemetry/instrumentation/hook.mjs', import.meta.url);

  const sdk = new NodeSDK({
    traceExporter: new OTLPTraceExporter(),
    metricReaders: [
      new PeriodicExportingMetricReader({
        exporter: new OTLPMetricExporter(),
        exportIntervalMillis: Number(env.OTEL_METRIC_EXPORT_INTERVAL ?? 60_000),
      }),
    ],
    logRecordProcessors: [new BatchLogRecordProcessor({ exporter: new OTLPLogExporter() })],
    instrumentations: [new PinoInstrumentation(), new RuntimeNodeInstrumentation()],
  });

  sdk.start();

  const shutdown = () => {
    sdk.shutdown().finally(() => process.exit(0));
  };
  process.on('SIGTERM', shutdown);
  process.on('SIGINT', shutdown);
}
