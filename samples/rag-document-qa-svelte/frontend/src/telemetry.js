/**
 * Browser OpenTelemetry for the Aspire dashboard.
 *
 * In run mode the Vite dev server (see vite.config.js) proxies same-origin `/otlp/*`
 * requests to the dashboard's OTLP/HTTP endpoint and adds the dashboard API key
 * server-side, so no secret reaches the browser and no dashboard CORS is needed.
 * In a production build `VITE_OTEL_ENABLED` is false and main.js never loads this module.
 *
 * Spans:
 *  - documentLoad / documentFetch / resourceFetch from the document-load instrumentation
 *  - one CLIENT span per `fetch('/api/...')`, which injects `traceparent` so the browser
 *    span becomes the parent of the API's server span (one end-to-end trace)
 *  - UI action spans ("ask question", "index sample documents", ...) created in App.svelte
 *    via the @opentelemetry/api tracer, which is a no-op until this SDK registers
 */
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto'
import { registerInstrumentations } from '@opentelemetry/instrumentation'
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load'
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch'
import { resourceFromAttributes } from '@opentelemetry/resources'
import { BatchSpanProcessor, StackContextManager, WebTracerProvider } from '@opentelemetry/sdk-trace-web'

/** Parses `key=value,key2=value2` (the OTEL_RESOURCE_ATTRIBUTES format). */
function parseDelimited(value) {
  return Object.fromEntries(
    value
      .split(',')
      .map((entry) => entry.split('=').map((part) => part.trim()))
      .filter(([key, val]) => key && val)
  )
}

export function initializeTelemetry() {
  const provider = new WebTracerProvider({
    resource: resourceFromAttributes({
      // Aspire's resource attributes (service.instance.id) let the dashboard
      // attribute browser spans to the `frontend` resource.
      ...parseDelimited(import.meta.env.VITE_OTEL_RESOURCE_ATTRIBUTES),
      'service.name': import.meta.env.VITE_OTEL_SERVICE_NAME
    }),
    spanProcessors: [
      new BatchSpanProcessor(new OTLPTraceExporter({ url: '/otlp/v1/traces' }), { scheduledDelayMillis: 1000 })
    ]
  })
  // StackContextManager carries the active UI span into fetch calls started inside traced().
  provider.register({ contextManager: new StackContextManager() })

  registerInstrumentations({
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        // Don't trace the exporter's own requests.
        ignoreUrls: [/\/otlp\//],
        clearTimingResources: true
      })
    ]
  })
}
