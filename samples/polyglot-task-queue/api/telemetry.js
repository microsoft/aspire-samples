import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { OTLPTraceExporter } from '@opentelemetry/exporter-otlp-grpc';
import { Resource } from '@opentelemetry/resources';
import { ATTR_SERVICE_NAME, ATTR_SERVICE_VERSION } from '@opentelemetry/semantic-conventions';

// Create OTLP exporter for Aspire
const traceExporter = new OTLPTraceExporter({
  url: process.env.OTEL_EXPORTER_OTLP_ENDPOINT || 'http://localhost:4317',
});

// Create SDK with automatic instrumentation
const sdk = new NodeSDK({
  resource: new Resource({
    [ATTR_SERVICE_NAME]: process.env.OTEL_SERVICE_NAME || 'api',
    [ATTR_SERVICE_VERSION]: '1.0.0',
  }),
  traceExporter,
  instrumentations: [
    getNodeAutoInstrumentations({
      // Automatically instrument Express, HTTP, and other Node.js libraries
      '@opentelemetry/instrumentation-fs': {
        enabled: false, // Disable fs instrumentation (too noisy)
      },
    }),
  ],
});

// Start the SDK
sdk.start();

console.log('📊 OpenTelemetry initialized');
console.log(`📤 Exporting to: ${process.env.OTEL_EXPORTER_OTLP_ENDPOINT || 'http://localhost:4317'}`);

// Graceful shutdown
process.on('SIGTERM', () => {
  sdk.shutdown()
    .then(() => console.log('📊 OpenTelemetry shut down'))
    .catch((error) => console.error('Error shutting down OpenTelemetry', error))
    .finally(() => process.exit(0));
});
