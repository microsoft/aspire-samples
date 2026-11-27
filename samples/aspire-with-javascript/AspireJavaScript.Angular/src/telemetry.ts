/**
 * Browser OpenTelemetry for the Aspire dashboard (run mode only).
 *
 * The Angular dev server (see proxy.conf.js) proxies same-origin `/otlp/*` requests to the dashboard's OTLP/HTTP
 * endpoint and adds the dashboard API key server-side, so no secret reaches the browser and the dashboard needs no
 * CORS configuration. Production builds never load this module.
 *
 * Spans:
 *  - documentLoad / documentFetch / resourceFetch from the document-load instrumentation
 *  - one CLIENT span per HttpClient request (HttpClient uses `fetch` via `withFetch()`), which injects `traceparent`
 *    so the API's server span joins the browser trace
 *  - "load shipment board" and "track shipment" UI spans created in AppComponent
 *
 * StackContextManager is used rather than ZoneContextManager: HttpClient's fetch backend issues the request inside
 * `NgZone.runOutsideAngular`, which would step outside an OpenTelemetry zone, while the request itself still starts
 * synchronously within `startActiveSpan`.
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
      // Aspire's resource attributes (service.instance.id) attribute browser spans to the `angular` resource.
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
        // Don't trace the exporter's own requests.
        ignoreUrls: [/\/otlp\//],
        clearTimingResources: true,
      }),
    ],
  });
}
