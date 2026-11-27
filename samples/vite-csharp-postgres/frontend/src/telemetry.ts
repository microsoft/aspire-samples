/**
 * Browser OpenTelemetry for the Aspire dashboard (run mode only).
 *
 * The Vite dev server (see vite.config.ts) proxies same-origin `/otlp/*` requests to the
 * dashboard's OTLP/HTTP endpoint and adds the dashboard API key server-side, so no secret
 * reaches the browser and the dashboard needs no CORS configuration. Production builds set
 * `VITE_OTEL_ENABLED` to false and main.tsx never loads this module.
 *
 * Spans:
 *  - documentLoad / documentFetch / resourceFetch from the document-load instrumentation
 *  - one CLIENT span per `fetch('/api/...')`, which injects `traceparent` so the API's server
 *    span, its servicedesk.* domain spans and the PostgreSQL spans join the same trace
 *  - UI action spans ("pick up request", "resolve request", ...) created in api.ts
 */
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchSpanProcessor, WebTracerProvider } from '@opentelemetry/sdk-trace-web';

/** Parses `key=value,key2=value2` (the OTEL_RESOURCE_ATTRIBUTES format). */
function parseDelimited(value: string): Record<string, string> {
  return Object.fromEntries(
    value
      .split(',')
      .map((entry) => entry.split('=').map((part) => part.trim()))
      .filter(([key, val]) => key && val)
  );
}

export function initializeTelemetry() {
  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      // Aspire's resource attributes (service.instance.id) let the dashboard
      // attribute browser spans to the `frontend` resource.
      ...parseDelimited(import.meta.env.VITE_OTEL_RESOURCE_ATTRIBUTES),
      'service.name': import.meta.env.VITE_OTEL_SERVICE_NAME,
    }),
    spanProcessors: [
      new BatchSpanProcessor(new OTLPTraceExporter({ url: '/otlp/v1/traces' }), { scheduledDelayMillis: 1000 }),
    ],
  });
  provider.register();

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        // Don't trace the exporter's own requests.
        ignoreUrls: [/\/otlp\//],
        clearTimingResources: true,
      }),
    ],
  });
}
