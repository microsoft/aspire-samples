/**
 * Browser OpenTelemetry for the Aspire dashboard (run mode only).
 *
 * The Angular dev server (see proxy.conf.cjs) proxies same-origin `/otlp/*` requests to the dashboard's OTLP/HTTP
 * endpoint and adds the dashboard API key server-side, so no secret reaches the browser and the dashboard needs no
 * CORS configuration. Production builds never load this module.
 *
 * Spans:
 *  - documentLoad / documentFetch / resourceFetch from the document-load instrumentation
 *  - one CLIENT span per HttpClient request (HttpClient uses `fetch` via `withFetch()`), which injects `traceparent`
 *    so the Spring Boot API's server span joins the browser trace
 *  - UI spans such as "book visit" and "load front desk" from src/app/tracing.ts
 *
 * StackContextManager is enough because the app is zoneless and every traced request starts synchronously inside
 * its UI span.
 */
import { context, propagation, trace } from '@opentelemetry/api';
import { W3CTraceContextPropagator } from '@opentelemetry/core';
import { OTLPTraceExporter } from '@opentelemetry/exporter-trace-otlp-proto';
import { registerInstrumentations } from '@opentelemetry/instrumentation';
import { DocumentLoadInstrumentation } from '@opentelemetry/instrumentation-document-load';
import { FetchInstrumentation } from '@opentelemetry/instrumentation-fetch';
import { resourceFromAttributes } from '@opentelemetry/resources';
import { BatchSpanProcessor, StackContextManager, TracerProvider } from '@opentelemetry/sdk-trace';

/** Non-secret settings served by the dev server at /otel-config.json. */
export interface TelemetryConfig {
  enabled: boolean;
  serviceName: string;
  resourceAttributes: Record<string, string>;
}

export function initializeTelemetry(config: TelemetryConfig): void {
  const provider = new TracerProvider({
    resource: resourceFromAttributes({
      // Aspire's resource attributes (service.instance.id) attribute browser spans to the `frontend` resource.
      ...config.resourceAttributes,
      'service.name': config.serviceName,
    }),
    spanProcessors: [
      new BatchSpanProcessor({
        exporter: new OTLPTraceExporter({ url: '/otlp/v1/traces' }),
        scheduledDelayMillis: 1000,
      }),
    ],
  });

  context.setGlobalContextManager(new StackContextManager().enable());
  propagation.setGlobalPropagator(new W3CTraceContextPropagator());
  trace.setGlobalTracerProvider(provider);

  registerInstrumentations({
    tracerProvider: provider,
    instrumentations: [
      new DocumentLoadInstrumentation(),
      new FetchInstrumentation({
        // Don't trace the exporter's own requests or the telemetry config.
        ignoreUrls: [/\/otlp\//, /\/otel-config\.json$/],
        clearTimingResources: true,
      }),
    ],
  });
}
